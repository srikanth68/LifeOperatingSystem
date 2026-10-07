using System.Text.Json;

namespace Vitara.Insight.Health;

// One lab value and the range it is read against, as numbers a picture can be drawn from.
//
// The words about a lab result ("came back at 121, above the usual range") were always
// available. The numbers behind them were stored in the finding's evidence and never reached
// the screen, so a result could be described but not shown. This carries them out.
//
// Read from evidence rather than recomputed, so the picture can never disagree with the
// sentence beside it: both come from the same stored record of what was found.
public record LabReading(
    double Value,
    double? Previous,
    double? Low,
    double? High,
    string? Unit,
    string? DrawnOn,
    string? Standing)
{
    // Null when there is nothing honest to draw: no evidence, a value that is not a number, or
    // no range at all. A gauge with no range would be a dot on an unlabelled line.
    public static LabReading? FromEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (Number(root, "value") is not { } value) return null;

            double? low = null, high = null;
            string? unit = null;
            if (Find(root, "range") is { ValueKind: JsonValueKind.Object } range)
            {
                low = Number(range, "low");
                high = Number(range, "high");
                unit = Text(range, "unit");
            }

            if (low is null && high is null) return null;

            return new LabReading(
                value, Number(root, "previous"), low, high, unit,
                Text(root, "drawnOn"), Text(root, "standing"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The writer used PascalCase for the range and camelCase for the rest; accept either
    // rather than depend on that staying true.
    private static JsonElement? Find(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static double? Number(JsonElement obj, string name) =>
        Find(obj, name) is { ValueKind: JsonValueKind.Number } n && n.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;

    private static string? Text(JsonElement obj, string name) =>
        Find(obj, name) is { ValueKind: JsonValueKind.String } s ? s.GetString() : null;
}
