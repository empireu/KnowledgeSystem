using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

// ReSharper disable UnusedAutoPropertyAccessor.Local

namespace KnowledgeSystem.Ai;

public sealed class ChatTemplatedLogprobGatingService : ILogprobGatingService
{
    private readonly HttpClient _httpClient = new();

    /// <summary>
    ///     Converts a system message and a user message into a single prompt string that ends right where the assistant should begin its response.
    /// </summary>
    public delegate string ChatTemplateFormatter(string systemMessage, string userMessage);

    /// <summary>
    ///     ChatML template.
    /// </summary>
    public static readonly ChatTemplateFormatter ChatMl = (system, user) => 
        $"<|im_start|>system\n{system}<|im_end|>\n<|im_start|>user\n{user}<|im_end|>\n<|im_start|>assistant\n";

    /// <summary>
    ///     Llama-3 / Llama-3.1 template.
    /// </summary>
    public static readonly ChatTemplateFormatter Llama3 = (system, user) =>
        $"<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\n{system}<|eot_id|>" +
        $"<|start_header_id|>user<|end_header_id|>\n\n{user}<|eot_id|>" +
        $"<|start_header_id|>assistant<|end_header_id|>\n\n";

    private readonly ChatTemplateFormatter _formatter;

    public ChatTemplatedLogprobGatingService(string endpoint, string apiKey, ChatTemplateFormatter? formatter = null)
    {
        _httpClient.BaseAddress = new Uri(endpoint);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _formatter = formatter ?? ChatMl;
    }

    public async Task<bool> IsRelevantAsync(string query, string document, double threshold, CancellationToken cancellationToken)
    {
        var userMessage = $"Given the search query: '{query}', is the following document relevant?\n\nDocument: '{document}'";

        return await ExecuteAsync("Answer only with a single word: YES or NO.", userMessage, threshold, cancellationToken);
    }

    public async Task<bool[]> AreRelevantAsync(string query, IReadOnlyList<string> documents, double threshold, CancellationToken cancellationToken)
    {
        var tasks = documents
            .Select(document => IsRelevantAsync(query, document, threshold, cancellationToken))
            .ToArray();

        var results = new bool[documents.Count];

        for (var index = 0; index < tasks.Length; index++)
        {
            results[index] = await tasks[index];
        }

        return results;
    }

    /// <summary>
    ///     Backwards-compatible overload: treats <paramref name="prompt"/> as the user message with a generic YES/NO system instruction.
    /// </summary>
    public Task<bool> ExecuteAsync(string prompt, double threshold, CancellationToken cancellationToken)
    {
        return ExecuteAsync("Answer only with a single word: YES or NO.", prompt, threshold, cancellationToken);
    }

    /// <summary>
    ///     Sends a chat-templated prompt to <c>/completion</c> and checks whether the YES token probability exceeds <paramref name="threshold"/>.
    /// </summary>
    public async Task<bool> ExecuteAsync(string systemMessage, string userMessage, double threshold, CancellationToken cancellationToken)
    {
        var probability = await ScoreAsync(systemMessage, userMessage, cancellationToken);
        return probability >= threshold;
    }

    public async Task<double> ScoreAsync(string systemMessage, string userMessage, CancellationToken cancellationToken)
    {
        var prompt = _formatter(systemMessage, userMessage);

        var requestBody = new Request
        {
            Prompt = prompt,
            NPredict = 1,
            Temperature = 0.0,
            NProbs = 10,
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

        return yesLogprob.HasValue ? Math.Exp(yesLogprob.Value) : 0.0;
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
