namespace KnowledgeSystem.Retrieval.Reranking;

public readonly struct RerankResult(int index, double relevanceScore)
{
    /// <summary>
    ///     The index of the result in the given corpus of documents.
    /// </summary>
    public readonly int Index = index;
    
    /// <summary>
    ///     The normalized relevance score, given by the model.
    /// </summary>
    public readonly double RelevanceScore = relevanceScore;
}