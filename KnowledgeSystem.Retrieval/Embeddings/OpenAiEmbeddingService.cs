using System.ClientModel;
using OpenAI;
using OpenAI.Embeddings;

namespace KnowledgeSystem.Retrieval.Embeddings;

/// <summary>
///     Embedding service wrapping the OpenAI SDK.
/// </summary>
public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly OpenAIClient _client;
    private readonly EmbeddingClient _embeddingClient;

    public int Dimension { get; }

    public OpenAiEmbeddingService(string endpoint, string modelId, string apiKey, int dimension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);

        Dimension = dimension;

        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
        };
        
        var credentials = new ApiKeyCredential(apiKey);
        
        _client = new OpenAIClient(credentials, options);
        _embeddingClient = _client.GetEmbeddingClient(modelId);
    }

    public async Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var response = await _embeddingClient.GenerateEmbeddingAsync(text, new EmbeddingGenerationOptions
        {
            Dimensions = Dimension,
        }, cancellationToken);

        return response.Value.ToFloats();
    }

    public async Task<ReadOnlyMemory<float>[]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            return [];
        }

        var response = await _embeddingClient.GenerateEmbeddingsAsync(texts, new EmbeddingGenerationOptions
        {
            Dimensions = Dimension,
        }, cancellationToken);
        
        var results = new ReadOnlyMemory<float>[texts.Count];

        for (var index = 0; index < response.Value.Count; index++)
        { 
            results[index] = response.Value[index].ToFloats();
        }

        return results;
    }

    public void Dispose()
    {
        // Empty
    }
}
