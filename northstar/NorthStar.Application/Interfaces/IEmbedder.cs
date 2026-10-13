namespace NorthStar.Application.Interfaces;

// Turns text into vectors, so recall can find a memory by meaning rather than by
// shared words: "my car" reaching "Honda Civic" with no alias set.
//
// Local only. The configured endpoint is a model on the user's own machine (an
// OpenAI-compatible /v1/embeddings, e.g. llama.cpp's server); memories are never sent
// to a cloud service to be embedded. When nothing is configured, Enabled is false and
// recall is exactly the keyword-and-entity search it was before.
public interface IEmbedder
{
    bool Enabled { get; }

    // Identifies the vectors' space. Vectors from a different model are not
    // comparable, so a model change re-embeds everything.
    string Model { get; }

    // isQuery: some embedding models expect a different prefix for a search query than
    // for a stored document. Null on any failure: recall then falls back, never fails.
    Task<float[][]?> EmbedAsync(IReadOnlyList<string> texts, bool isQuery, CancellationToken ct = default);
}
