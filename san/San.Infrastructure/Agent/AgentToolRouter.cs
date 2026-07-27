using Microsoft.Extensions.Logging;
using San.Application.Interfaces;

namespace San.Infrastructure.Agent;

// Chooses the tool surface for a chat turn: the full Maaya.Mcp catalog when the
// gateway is reachable (~40 tools — everything an external agent gets), else the
// small built-in registry so chat never breaks just because one container is down.
public class AgentToolRouter(McpToolClient mcp, AgentToolExecutor localExecutor, ILogger<AgentToolRouter> logger)
{
    public async Task<(List<ToolDefinition> Tools, Func<ToolCall, CancellationToken, Task<string>> Executor)>
        ResolveAsync(CancellationToken ct = default)
    {
        var mcpTools = await mcp.TryListToolsAsync(ct);
        if (mcpTools is { Count: > 0 })
        {
            logger.LogInformation("Agent tools: Maaya.Mcp catalog ({Count} tools)", mcpTools.Count);
            return (mcpTools, mcp.CallToolAsync);
        }

        logger.LogWarning("Agent tools: MCP gateway unreachable — using built-in registry");
        return (AgentToolRegistry.GetTools(), localExecutor.ExecuteAsync);
    }
}
