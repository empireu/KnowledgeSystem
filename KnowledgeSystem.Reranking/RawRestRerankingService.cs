using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable once ClassNeverInstantiated.Global

namespace KnowledgeSystem.Reranking;

public sealed class RawRestRerankingService : IRerankingService
{
    private readonly HttpClient _httpClient = new();
    private readonly ILogger<RawRestRerankingService> _logger;
    private readonly string _modelId;

    public RawRestRerankingService(ILogger<RawRestRerankingService> logger, string endpoint, string modelId, string apiKey)
    {
        _logger = logger;
        _modelId = modelId;
        _httpClient.BaseAddress = new Uri(endpoint);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<RerankResult[]?> RerankAsync(string query, List<string> documents, int topN)
    {
        if (topN <= 0 || topN > documents.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(topN));
        }
        
        var requestBody = new RerankRequest
        {
            Model = _modelId,
            Query = query,
            Documents = documents,
            TopN = topN
        };
        
        var response = await _httpClient.PostAsJsonAsync("/rerank", requestBody);
       
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadFromJsonAsync<JsonRerankResponse>();

        if (content == null)
        {
            _logger.LogError("Could not parse reranking response");
            return null;
        }

        var jsonRerankResults = content.Results;
        
        if (jsonRerankResults == null)
        {
            _logger.LogError("Reranking response had null results");
            return null;
        }

        if (jsonRerankResults.Count != topN)
        {
            _logger.LogError("Reranking response had {returnedCount} results instead of required {param}", jsonRerankResults.Count, topN);
            return null;
        }

        var results = new RerankResult[topN];

        for (var index = 0; index < jsonRerankResults.Count; index++)
        {
            var result = jsonRerankResults[index];
            
            results[index] = new RerankResult(result.Index, result.RelevanceScore);
        }
        
        results.Sort(results, CompareResults);
        
        return results;
    }

    private static int CompareResults(RerankResult a, RerankResult b) => b.RelevanceScore.CompareTo(a.RelevanceScore);

    private sealed class RerankRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("query")]
        public required string Query { get; init; }

        [JsonPropertyName("documents")]
        public required List<string> Documents { get; init; }

        [JsonPropertyName("top_n")]
        public required int TopN { get; init; }
    }
    
    private sealed class JsonRerankResponse
    {
        [JsonPropertyName("results")]
        public List<JsonRerankResult>? Results { get; set; } = [];
    }

    public sealed class JsonRerankResult
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }
    
        [JsonPropertyName("relevance_score")]
        public double RelevanceScore { get; set; }
    }
}