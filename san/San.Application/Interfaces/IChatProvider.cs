namespace San.Application.Interfaces;

public record ChatTurn(string Role, string Content);

public record ToolDefinition(string Name, string Description, Dictionary<string, ToolParameter> Parameters);
public record ToolParameter(string Type, string Description, bool Required = false);
public record ToolCall(string Name, Dictionary<string, string> Arguments);
public record AgentStep(string? Text, ToolCall? ToolCall);

public interface IChatProvider
{
    string ProviderName { get; }
    string ModelName { get; }

    // True for agent backends (e.g. Hermes) that run their own tool-calling loop and
    // execute actions themselves. When true, San must NOT append its prose "emit a JSON
    // action block" instructions, nor post-process the reply looking for one — the agent
    // already did the real work via its own (MCP) tools.
    bool HandlesToolsNatively => false;

    Task<string> CompleteAsync(string systemPrompt, List<ChatTurn> history, CancellationToken ct = default);

    // Agent loop — returns final text after resolving all tool calls.
    // Default impl falls back to simple CompleteAsync (no tool use).
    Task<string> CompleteWithToolsAsync(
        string systemPrompt,
        List<ChatTurn> history,
        List<ToolDefinition> tools,
        Func<ToolCall, CancellationToken, Task<string>> toolExecutor,
        int maxSteps = 10,
        CancellationToken ct = default)
    {
        return CompleteAsync(systemPrompt, history, ct);
    }
}
