namespace KnowledgeSystem.Ai;

public enum ProviderType
{
    /// <summary>
    ///     Tested on OpenRouter and some other quick tests.
    /// </summary>
    Usual,
    /// <summary>
    ///     Deepseek requires piping the reasoning content or it fails; not implemented yet.
    /// </summary>
    Deepseek
}