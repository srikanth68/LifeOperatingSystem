using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maaya.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.Interfaces;

namespace San.Worker;

// San's daily pass over bank transactions nothing has claimed.
//
// The recurring matcher in Aasthi handles rent, mortgage and HOA: those follow a rule,
// and a rule is cheaper and steadier than a model. What no rule can ever catch is the
// long tail -- a hardware store run, a plumber, a permit fee -- because nothing in a
// bank description says which property it was for, or whether it was for a property at
// all. That needs to know whose life this is, and it is the one part of this pipeline
// where a language model genuinely earns its place.
//
// It proposes and stops. Nothing here writes a confirmed entry, because the output is
// tax records: an unclassified expense costs the user a minute in April, while a
// silently miscategorised capital improvement is a wrong answer to an auditor three
// years later, by which time the receipt is gone.
//
// And it never notifies. The user's own words about the reminders were that they were
// becoming the thing they wanted to ignore -- a daily "I found 3 transactions" ping is
// exactly how this becomes that. The queue sits in Aasthi until they look.
public class PropertyLedgerWorker(IServiceProvider services, ILogger<PropertyLedgerWorker> logger) : BackgroundService
{
    // Daily, and only just worth that. Vault syncs once a day, so running more often
    // re-reads the same rows and spends the model on nothing.
    private static readonly TimeSpan Interval = TimeSpan.FromHours(
        double.TryParse(Environment.GetEnvironmentVariable("PROPERTY_LEDGER_INTERVAL_HOURS"), out var h) && h > 0 ? h : 24);

    // Offset from the other daily workers so they are not all competing for the one
    // llama.cpp slot at the same minute.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(17);

    private const string Prompt =
        "You are San. Below are the user's properties, and bank transactions that have not been " +
        "assigned to any of them. Decide which transactions are costs of a specific property.\n\n" +
        "RULES:\n" +
        "- Use ONLY the transactionId and propertyId values exactly as given. Never invent an id, " +
        "never abbreviate one, never write an address where an id belongs.\n" +
        "- Most transactions are ordinary personal spending and belong to NO property. Skip them. " +
        "Returning an empty list is a good answer and the most common correct one.\n" +
        "- Only pair a transaction with a property when the description genuinely points to it: a " +
        "contractor, a hardware store, a permit, a utility for that address. A supermarket is not a " +
        "property expense merely because a property exists.\n" +
        "- If a transaction is clearly a property cost but you cannot tell WHICH property, skip it. " +
        "The user will assign it. A guess costs more than a gap.\n" +
        "- Do not report amounts or dates. Those are read from the real transaction.\n\n" +
        "Return ONLY a JSON array, no prose and no code fence:\n" +
        "[{\"transactionId\":\"...\",\"propertyId\":\"...\",\"category\":\"repair|improvement|supplies|utility|insurance|tax|hoa|other\",\"reason\":\"a few words\"}]\n\n" +
        "If nothing belongs to a property, return exactly: []";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Property ledger worker started. Interval: {h}h (first run in {m}m)",
            Interval.TotalHours, StartupDelay.TotalMinutes);

        try { await Task.Delay(StartupDelay, ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Property ledger pass failed."); }

            try { await Task.Delay(Interval, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        var chat = scope.ServiceProvider.GetRequiredService<IChatProvider>();

        var aasthi = httpFactory.CreateClient("aasthi");
        var token = tokens.GenerateAccessToken("san-service", "san");

        // Deterministic first. Anything the recurring matcher can claim should be
        // claimed by arithmetic before the model is asked to look at what is left.
        var reconciled = await PostAsync(aasthi, token, "/api/ledger/reconcile", null, ct);
        if (reconciled is not null) logger.LogInformation("Reconcile: {Result}", reconciled);

        var unassigned = await GetArrayAsync(aasthi, token, "/api/ledger/unassigned?days=7", ct);
        if (unassigned.Count == 0)
        {
            logger.LogInformation("Property ledger: nothing unassigned this pass.");
            return;
        }

        var properties = await GetArrayAsync(aasthi, token, "/api/properties", ct);
        if (properties.Count == 0)
        {
            logger.LogInformation("Property ledger: no properties configured, nothing to assign against.");
            return;
        }

        var realTx = unassigned.Select(t => Str(t, "id")).Where(x => x is not null).Select(x => x!).ToHashSet();
        var realProps = properties.Select(p => Str(p, "id")).Where(x => x is not null).Select(x => x!).ToHashSet();

        var reply = await chat.CompleteAsync(Prompt, [new ChatTurn("user", BuildPayload(properties, unassigned))], ct);

        var grounded = PropertyExpenses.Grounded(PropertyExpenses.Parse(reply), realTx, realProps);
        if (grounded.Count == 0)
        {
            logger.LogInformation("Property ledger: {Count} unassigned, none proposed.", unassigned.Count);
            return;
        }

        var written = 0;
        foreach (var p in grounded)
        {
            // Amount and date come off the REAL transaction, never off the model's
            // reply. It was not asked for them and must not be able to supply them.
            var tx = unassigned.First(t => Str(t, "id") == p.TransactionId);
            if (!Guid.TryParse(p.PropertyId, out var propertyId)) continue;

            var body = new
            {
                vaultTransactionId = p.TransactionId,
                propertyId,
                amount = tx.GetProperty("amount").GetDecimal(),
                date = Str(tx, "date"),
                type = Str(tx, "direction") == "in" ? "income" : "expense",
                category = p.Category,
                reason = p.Reason,
            };

            // A 409 means the user got there first, which is the system working.
            if (await PostAsync(aasthi, token, "/api/ledger/propose", body, ct) is not null) written++;
        }

        logger.LogInformation(
            "Property ledger: {Unassigned} unassigned, {Proposed} proposed, {Written} queued for review.",
            unassigned.Count, grounded.Count, written);
    }

    // Only what the model needs to pair things up. Addresses identify a property to a
    // reader; ids are what it must echo back.
    private static string BuildPayload(List<JsonElement> properties, List<JsonElement> transactions)
    {
        var props = properties.Select(p =>
            $"- id={Str(p, "id")} | {Str(p, "address")}, {Str(p, "city")} {Str(p, "state")}");

        var txs = transactions.Select(t =>
            $"- id={Str(t, "id")} | {Str(t, "date")} | {t.GetProperty("amount").GetDecimal():0.00} " +
            $"{Str(t, "direction")} | {Str(t, "description")}");

        return "PROPERTIES:\n" + string.Join("\n", props) +
               "\n\nUNASSIGNED TRANSACTIONS:\n" + string.Join("\n", txs);
    }

    private async Task<List<JsonElement>> GetArrayAsync(HttpClient http, string token, string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Aasthi returned HTTP {Status} for {Url}.", (int)resp.StatusCode, url);
                return [];
            }

            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return doc.ValueKind == JsonValueKind.Array ? doc.EnumerateArray().ToList() : [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not reach Aasthi for {Url}.", url);
            return [];
        }
    }

    private async Task<string?> PostAsync(HttpClient http, string token, string url, object? body, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) req.Content = JsonContent.Create(body);

            using var resp = await http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsStringAsync(ct);

            logger.LogInformation("Aasthi returned HTTP {Status} for {Url}.", (int)resp.StatusCode, url);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not POST to Aasthi {Url}.", url);
            return null;
        }
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
