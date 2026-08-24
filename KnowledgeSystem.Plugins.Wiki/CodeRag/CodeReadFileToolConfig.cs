namespace KnowledgeSystem.Plugins.Wiki.CodeRag;

public sealed class CodeReadFileToolConfig
{
    public int MaxLines { get; set; } = 500;

    public int MaxChars { get; set; } = 1024 * 16;

    public int MaxLineLength { get; set; } = 500;
}
