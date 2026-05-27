using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Ai;

public class ChatOptionsConfig
{
    /// <summary>
    ///     For OpenRouter only. Null defaults to the account's routing options.
    /// </summary>
    public string? ProviderOnly { get; set; }
    
    /// <summary>
    ///     The used temperature. Null defaults to whatever the provider has.
    /// </summary>
    public float? Temperature { get; set; }
    
    /// <summary>
    ///     The used reasoning effort. Null defaults to whatever the provider has.
    /// </summary>
    public string? ReasoningEffort { get; set; }

    public ChatOptions CreateOptions()
    {
        var options = new ChatOptions();

        if (Temperature.HasValue)
        {
            options.Temperature = Temperature.Value;
        }

        if (!string.IsNullOrWhiteSpace(ProviderOnly) || !string.IsNullOrWhiteSpace(ReasoningEffort))
        {
            options.AdditionalProperties = new AdditionalPropertiesDictionary();
        }

        if (!string.IsNullOrWhiteSpace(ProviderOnly))
        {
            options.AdditionalProperties!["provider"] = new Dictionary<string, object>
            {
                ["only"] = new[] { ProviderOnly }
            };
        }

        if (!string.IsNullOrWhiteSpace(ReasoningEffort))
        {
            options.AdditionalProperties!["reasoning_effort"] = ReasoningEffort;
        }

        return options;
    }
}