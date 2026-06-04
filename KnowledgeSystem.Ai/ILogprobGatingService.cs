namespace KnowledgeSystem.Ai;

public interface ILogprobGatingService
{
    /// <summary>
    ///     Checks if the <see cref="document"/> is relevant to the <see cref="query"/> using an LLM filter.
    ///     Specifically, it restrains the model to output yes or no, and checks the logprobes to filter against the <see cref="threshold"/> (the threshold itself is not logarithmic).
    /// </summary>
    /// <returns>True if the document is relevant. Otherwise, false.</returns>
    public Task<bool> IsRelevantAsync(string query, string document, double threshold, CancellationToken cancellationToken);
    
    /// <summary>
    ///     Batch version of <see cref="IsRelevantAsync"/>.
    /// </summary>
    public Task<bool[]> AreRelevantAsync(string query, IReadOnlyList<string> documents, double threshold, CancellationToken cancellationToken);

    /// <summary>
    ///     Executes the LLM with a custom crafted prompt for gating.
    /// </summary>
    /// <returns></returns>
    public Task<bool> ExecuteAsync(string prompt, double threshold, CancellationToken cancellationToken);
}