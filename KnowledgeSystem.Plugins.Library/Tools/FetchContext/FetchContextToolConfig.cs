namespace KnowledgeSystem.Plugins.Library.Tools.FetchContext;

public sealed class FetchContextToolConfig
{
    /// <summary>
    ///     Maximum number of references per call.
    /// </summary>
    public int MaxRefs { get; set; } = 10;

    /// <summary>
    ///     Maximum total output characters.
    /// </summary>
    public int MaxChars { get; set; } = 8192;
}
