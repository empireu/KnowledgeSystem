namespace KnowledgeSystem.Plugins.Wiki.CodeRag;

public sealed class CodeGrepToolConfig
{
    public int MaxResults { get; set; } = 20;

    public int SnippetLength { get; set; } = 80;

    public int MaxMatchesPerFile { get; set; } = 5;

    public int MaxFileBytes { get; set; } = 5 * 1024 * 1024;
}
