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
}