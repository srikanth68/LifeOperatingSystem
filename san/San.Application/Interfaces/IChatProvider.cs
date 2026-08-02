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

    // True for providers with native tool calling (e.g. llamacpp-agent) that run a real tool loop and
    // execute actions themselves. When true, San must NOT append its prose "emit a JSON
    // action block" instructions, nor post-process the reply looking for one — the agent
    // already did the real work via its own (MCP) tools.
    bool HandlesToolsNatively => false;

    Task<string> CompleteAsync(string systemPrompt, List<ChatTurn> history, CancellationToken ct = default);

    // Agent loop — returns final text after resolving all tool calls.
    // Default impl falls back to simple CompleteAsync (no tool use).
    //
    // enableThinking: some local models emit a step-by-step deliberation before
    // deciding whether/which tool to call. It costs roughly 15x the generation
    // tokens, so interactive chat leaves it off (latency is the experience there),
    // while background work that has to read and judge real content — email triage —
    // turns it on, since nobody is waiting on it.
    Task<string> CompleteWithToolsAsync(
        string systemPrompt,
        List<ChatTurn> history,
        List<ToolDefinition> tools,
        Func<ToolCall, CancellationToken, Task<string>> toolExecutor,
        int maxSteps = 10,
        bool enableThinking = false,
        CancellationToken ct = default)
    {
        return CompleteAsync(systemPrompt, history, ct);
    }
}
