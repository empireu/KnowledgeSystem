namespace KnowledgeSystem.Plugins.Wiki.CodeRag;

public sealed class CodeReadFileToolConfig
{
    public int MaxLines { get; set; } = 200;

    public int MaxChars { get; set; } = 8192;

    public int MaxLineLength { get; set; } = 500;
}
