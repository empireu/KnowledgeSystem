using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

// ReSharper disable UnusedAutoPropertyAccessor.Local

namespace KnowledgeSystem.Embedding;

/// <summary>
///     An embedding service that uses the OpenAI API.
///     This is meant to be used with a local model that exposes an OpenAI-compatible API.
/// </summary>
public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _modelId;
    private readonly string? _prompt;
    private readonly int _dimension;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public OpenAiEmbeddingService(string endpoint, string apiKey, string modelId, int dimension = 1024, string? prompt = null)
    {
        _prompt = prompt;
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);

        _dimension = dimension;
        Dimension = dimension;
        _modelId = modelId;

        var baseUri = endpoint.TrimEnd('/');
        if (baseUri.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            baseUri = baseUri[..^3];
        }
        
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUri.TrimEnd('/') + "/v1/"),
        };
        
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public int Dimension { get; }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private string ProcessQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(_prompt))
        {
            return query;
        }

        return _prompt + query;
    }

    public async Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var results = await EmbedBatchAsync([text], cancellationToken);
        return results[0];
    }

    public async Task<ReadOnlyMemory<float>[]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            return [];
        }

        var queries = texts.Select(ProcessQuery).ToList();

        var requestBody = new EmbeddingRequest
        {
            Model = _modelId,
            Input = queries,
            Dimensions = _dimension > 0 ? _dimension : null,
        };

        var response = await _httpClient.PostAsJsonAsync("embeddings", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Embedding request failed with status {response.StatusCode}: {errorBody}");
        }

        var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);

        var result = JsonSerializer.Deserialize<EmbeddingResponse>(rawResponse, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize embedding response.");

        if (result.Data == null || result.Data.Length == 0)
        {
            throw new InvalidOperationException($"Embedding response contained no data. Raw response: {rawResponse}");
        }

        var results = new ReadOnlyMemory<float>[result.Data.Length];
        for (var i = 0; i < result.Data.Length; i++)
        {
            var embedding = result.Data[i].Embedding;
            
            if (embedding == null)
            {
                throw new InvalidOperationException($"Embedding at index {i} was null.");
            }
            
            IEmbeddingService.SanitizeNetworkResult(embedding);

            results[i] = new ReadOnlyMemory<float>(embedding);
        }

        return results;
    }

    private sealed class EmbeddingRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("input")]
        public List<string> Input { get; set; } = [];

        [JsonPropertyName("dimensions")]
        public int? Dimensions { get; set; }
    }

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public EmbeddingItem[]? Data { get; set; }
    }

    private sealed class EmbeddingItem
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
