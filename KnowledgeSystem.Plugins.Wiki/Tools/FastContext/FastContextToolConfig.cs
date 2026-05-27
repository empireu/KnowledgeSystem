namespace KnowledgeSystem.Plugins.Wiki.Tools.FastContext;

public sealed class FastContextToolConfig
{
    /// <summary>
    ///     Parameter for: <see cref="FastContextRetrievalPipeline.Description.BootstrapCount"/>.
    /// </summary>
    public int BootstrapCount { get; set; } = 15;

    /// <summary>
    ///     Parameter for: <see cref="FastContextRetrievalPipeline.Description.Parameter"/>.
    /// </summary>
    public float Parameter { get; set; }= 5;

    /// <summary>
    ///     The maximum number of vector search results to retrieve from HNSW.
    /// </summary>
    public int MaxResults { get; set; } = 100;

    /// <summary>
    ///     Max number of matches to search for per body.
    /// </summary>
    public int MaxWindows { get; set; } = 10;
    
    /// <summary>
    ///     For compact extraction, this is the approximate size of the surrounding context, in characters, for each match.
    /// </summary>
    public int SnippetContext { get; set; } = 35;
    
    /// <summary>
    ///     Desired number of snippets total. If the number of tokens exceeds this, then one snippet per token will be included (exceeding this count).
    /// </summary>
    public int DesiredSnippets { get; set; } = 4;

    /// <summary>
    ///     Parameter for: <see cref="FastContextRetrievalPipeline.Description.Bm25Results"/>.
    /// </summary>
    public int Bm25Results { get; set; } = 30;

    /// <summary>
    ///     The significance level for <see cref="FastContextRetrievalPipeline.ExtractGapTokens"/>.
    /// </summary>
    public float SignificanceLevel { get; set; }= 0.05f;
}