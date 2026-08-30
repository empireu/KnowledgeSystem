namespace KnowledgeSystem.Ai;

public enum ProviderType
{
    /// <summary>
    ///     Tested on OpenRouter and some other quick tests.
    /// </summary>
    Usual,
    /// <summary>
    ///     Deepseek. Thinking mode requires the reasoning content to be passed back on every request that carries tools.
    /// </summary>
    Deepseek
}