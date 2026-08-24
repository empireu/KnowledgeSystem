namespace KnowledgeSystem.Plugins.Wiki.CodeRag;

public sealed class CodeRipgrepToolConfig
{
    public string? RipgrepPath { get; set; }

    public int MaxOutputChars { get; set; } = 32768;

    public int TimeoutSeconds { get; set; } = 30;
}
