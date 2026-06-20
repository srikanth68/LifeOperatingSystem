namespace San.Application.Interfaces;

public record ChatTurn(string Role, string Content);

// Abstraction over the underlying LLM so the active provider/model is a config choice
// (Llm:Provider / Llm:Model in appsettings or env vars), not a code change. Add a new
// implementation + a DI switch case in Program.cs to support another provider later.
public interface IChatProvider
{
    string ProviderName { get; }
    string ModelName { get; }

    Task<string> CompleteAsync(string systemPrompt, List<ChatTurn> history, CancellationToken ct = default);
}
