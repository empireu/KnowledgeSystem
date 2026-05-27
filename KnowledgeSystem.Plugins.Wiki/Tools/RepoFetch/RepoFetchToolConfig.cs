namespace KnowledgeSystem.Plugins.Wiki.Tools.RepoFetch;

public sealed class RepoFetchToolConfig
{
    /// <summary>
    ///     Maximum number of characters to fetch.
    ///     Gives off a warning without any content when the requested range exceeds this.
    /// </summary>
    public int MaxChars { get; set; } = 8192;
}