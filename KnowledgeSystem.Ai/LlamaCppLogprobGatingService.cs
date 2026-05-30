using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

// ReSharper disable UnusedAutoPropertyAccessor.Local

namespace KnowledgeSystem.Ai;

public sealed class LlamaCppLogprobFilteringService : ILogprobGatingService
{
    private readonly HttpClient _httpClient = new();

    public LlamaCppLogprobFilteringService(string endpoint, string apiKey)
    {
        _httpClient.BaseAddress = new Uri(endpoint);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<bool> IsRelevantAsync(string query, string document, double threshold, CancellationToken cancellationToken)
    {
        var prompt = $"Given the search query: '{query}', is the following document relevant? " +
                     $"Answer only with a single word: YES or NO.\n\n" +
                     $"Document: '{document}'\n\n" +
                     $"Answer:";

        var requestBody = new Request
        {
            Prompt = prompt,
            NPredict = 1,       
            Temperature = 0.0,  
            NProbs = 5,         
            Stream = false
        };

        var response = await _httpClient.PostAsJsonAsync(
            "/completion",
            requestBody, 
            cancellationToken: cancellationToken
        );
        
        response.EnsureSuccessStatusCode();

        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var root = JsonNode.Parse(rawJson);

        if (root == null)
        {
            throw new Exception($"Failed to parse JSON response. Raw content: {rawJson}");
        }

        var completionProbabilities = root["completion_probabilities"]?.AsArray();
        if (completionProbabilities == null || completionProbabilities.Count == 0)
        {
            throw new Exception("Completion probabilities were missing or empty");
        }

        var topLogprobs = completionProbabilities[0]?["top_logprobs"]?.AsArray();
        if (topLogprobs == null)
        {
            throw new Exception("Top logprobs were missing or empty");
        }

        double? yesLogprob = null;
        
        foreach (var item in topLogprobs)
        {
            var tokenStr = item?["token"]?.ToString();
            
            if (!string.IsNullOrWhiteSpace(tokenStr) && tokenStr.Trim().Equals("YES", StringComparison.OrdinalIgnoreCase))
            {
                yesLogprob = item?["logprob"]?.GetValue<double>();
                break;
            }
        }

        if (!yesLogprob.HasValue)
        {
            return false; 
        }
        
        return Math.Exp(yesLogprob.Value) >= threshold;
    }

    public async Task<bool[]> AreRelevantAsync(string query, IReadOnlyList<string> documents, double threshold, CancellationToken cancellationToken)
    {
        var tasks = documents
            .Select(document => IsRelevantAsync(query, document, threshold, cancellationToken))
            .ToArray();

        var results = new bool[documents.Count];

        for (var index = 0; index < tasks.Length; index++)
        {
            var task = tasks[index];
            
            results[index] = await task;
        }
        
        return results;
    }

    private sealed class Request
    {
        [JsonPropertyName("prompt")]
        public required string Prompt { get; init; }

        [JsonPropertyName("n_predict")]
        public required int NPredict { get; init; }

        [JsonPropertyName("temperature")]
        public required double Temperature { get; init; }

        [JsonPropertyName("n_probs")]
        public required int NProbs { get; init; }

        [JsonPropertyName("stream")]
        public required bool Stream { get; init; }
    }
}