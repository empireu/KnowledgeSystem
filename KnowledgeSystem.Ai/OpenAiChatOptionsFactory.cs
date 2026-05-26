using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Ai;

/// <summary>
///     Centralizes provider-specific request parameters for OpenAI-backed clients.
/// </summary>
public static class OpenAiChatOptionsFactory
{
    /// <summary>
    ///     Creates a chat request options with the specified extra parameters.
    /// </summary>
    /// <param name="providerOnly">The wanted provider (tested on OpenRouter only).</param>
    /// <param name="temperature">The temperature, if supported (tested on OpenRouter only).</param>
    /// <param name="reasoningEffort">The reasoning effort, if supported (not tested).</param>
    /// <returns></returns>
    public static ChatOptions Create(
        string? providerOnly = null,
        float? temperature = null,
        string? reasoningEffort = null)
    {
        var options = new ChatOptions();

        if (temperature.HasValue)
        {
            options.Temperature = temperature.Value;
        }

        if (!string.IsNullOrWhiteSpace(providerOnly) || !string.IsNullOrWhiteSpace(reasoningEffort))
        {
            options.AdditionalProperties = new AdditionalPropertiesDictionary();
        }

        if (!string.IsNullOrWhiteSpace(providerOnly))
        {
            options.AdditionalProperties!["provider"] = new Dictionary<string, object>
            {
                ["only"] = new[] { providerOnly }
            };
        }

        if (!string.IsNullOrWhiteSpace(reasoningEffort))
        {
            options.AdditionalProperties!["reasoning_effort"] = reasoningEffort;
        }

        return options;
    }
}
