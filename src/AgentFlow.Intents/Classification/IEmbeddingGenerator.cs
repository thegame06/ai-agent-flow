namespace AgentFlow.Intents.Classification;

/// <summary>
/// Generates vector embeddings for text content.
/// Abstraction layer over different embedding models (OpenAI, Azure, local, etc.).
/// </summary>
public interface IEmbeddingGenerator
{
    /// <summary>
    /// Generates a vector embedding for the given text.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of float values representing the embedding vector.</returns>
    Task<IReadOnlyList<float>> GenerateAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// The dimension of the embeddings produced by this generator.
    /// Common values: 1536 (OpenAI text-embedding-3-small), 384 (all-MiniLM-L6-v2).
    /// </summary>
    int Dimension { get; }

    /// <summary>
    /// The name/identifier of the embedding model being used.
    /// For observability and debugging purposes.
    /// </summary>
    string ModelName { get; }
}
