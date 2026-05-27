namespace KnowledgeSystem.Plugins.Library.Tools.GrepContent;

public sealed class GrepContentToolConfig
{
    /// <summary>
    ///     Maximum number of matching files to show.
    /// </summary>
    public int MaxResults { get; set; } = 20;

    /// <summary>
    ///     Maximum length of each inline snippet in characters.
    /// </summary>
    public int SnippetLength { get; set; } = 60;

    /// <summary>
    ///     Maximum number of ranges to show.
    /// </summary>
    public int MaximumRanges { get; set; } = 3;
}
