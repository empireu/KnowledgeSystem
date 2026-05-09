namespace KnowledgeSystem.Retrieval.Embeddings;

/// <summary>
///     Abstraction over an embedding generation service.
///     We'll most likely be using a local model for this, let's be real.
/// </summary>
public interface IEmbeddingService : IDisposable
{
    /// <summary>
    ///     The dimension of the vectors produced by this service.
    /// </summary>
    int Dimension { get; }

    /// <summary>
    ///     Generates an embedding vector for the given text.
    /// </summary>
    Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Generates embedding vectors for a batch of texts.
    /// </summary>
    Task<ReadOnlyMemory<float>[]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}
