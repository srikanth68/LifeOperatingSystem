using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using San.Application.Interfaces;

namespace San.API.Controllers;

// The training set, as it accumulates.
//
// Nothing here is used by the running system. It exists so that the record San now
// keeps of its own behaviour can be looked at, counted, and eventually fed to a
// fine-tune -- and so the question "do I have enough data yet?" has an answer that is
// not a guess.
[ApiController, Route("api/turnlog")]
public class TurnLogController(ISanRepository repo, ILogger<TurnLogController> logger) : ControllerBase
{
    // Is this worth training on yet, and what is in it.
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var total = await repo.CountTurnLogsAsync();
        var recent = await repo.GetTurnLogsAsync(take: 20000);

        var withTools = recent.Count(t => t.ToolCallCount > 0);
        var claimed = recent.Count(t => t.ClaimedUnverifiedWrite);

        return Ok(new
        {
            total,
            // Turns that actually called something are the ones a tool-calling fine-tune
            // learns from. A thousand turns of chit-chat teach it nothing about tools.
            withToolCalls = withTools,
            // Every one of these is a labelled failure: the model announced a write and
            // the tool record disagrees. No annotation needed.
            announcedWriteButDidNot = claimed,
            byMode = recent.GroupBy(t => t.Source).ToDictionary(g => g.Key, g => g.Count()),
            oldest = recent.Count > 0 ? recent.Min(t => t.CreatedAt) : (DateTime?)null,
            newest = recent.Count > 0 ? recent.Max(t => t.CreatedAt) : (DateTime?)null,
        });
    }

    // JSONL, one turn per line.
    //
    // Deliberately NOT shaped into any trainer's chat format. Which template a fine-tune
    // wants depends on the tool that runs it, and baking a guess in here would mean
    // re-exporting when that guess is wrong. The fields are the facts; the training
    // script decides what they look like.
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] int take = 5000,
        [FromQuery] bool failuresOnly = false,
        [FromQuery] bool withToolsOnly = false)
    {
        var logs = await repo.GetTurnLogsAsync(take, failuresOnly);
        if (withToolsOnly) logs = logs.Where(t => t.ToolCallCount > 0).ToList();

        var sb = new StringBuilder();
        foreach (var t in logs.OrderBy(x => x.CreatedAt))
        {
            sb.AppendLine(JsonSerializer.Serialize(new
            {
                created_at = t.CreatedAt,
                source = t.Source,
                model = t.Model,
                user = t.UserMessage,
                assistant = t.AssistantReply,
                tools_offered = t.OfferedTools.Split(',', StringSplitOptions.RemoveEmptyEntries),
                tool_calls = JsonDocument.Parse(string.IsNullOrWhiteSpace(t.ToolCallsJson) ? "[]" : t.ToolCallsJson).RootElement,
                claimed_unverified_write = t.ClaimedUnverifiedWrite,
                llm_ms = t.LlmMs,
            }));
        }

        logger.LogInformation("Exported {Count} turn logs.", logs.Count);

        // A file rather than a JSON body: this is meant to be saved and fed to a
        // training script, and it contains the user's own conversations -- which is
        // exactly why it stays on their machine and behind the same auth as everything
        // else here.
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "application/x-ndjson",
            $"san-turns-{DateTime.UtcNow:yyyyMMdd}.jsonl");
    }
}
