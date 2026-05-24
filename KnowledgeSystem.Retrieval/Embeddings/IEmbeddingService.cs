using System.Diagnostics;

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
    
    /// <summary>
    ///     Sanitizes the raw embedding by clamping near-zero components to zero, then normalizing.
    ///     Very small components might cause issues with SIMD calculations.
    /// </summary>
    public static void SanitizeNetworkResult(Span<float> vector)
    {
        var normSqr = 0.0f;
    
        for (var i = 0; i < vector.Length; i++)
        {
            var value = vector[i];
            
            if (MathF.Abs(value) < 1e-10f)
            {
                vector[i] = 0f;
            }
            else
            {
                normSqr += value * value;
            }
        }

        var norm = MathF.Sqrt(normSqr);

        if (!double.IsNaN(norm) && !double.IsInfinity(norm) && norm > 0.0f)
        {
            var recipNorm = 1.0f / norm;
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] *= recipNorm;
            }
        }
        else
        {
            Debug.Fail($"Got embedding with norm {norm}");
        }
    }
}
