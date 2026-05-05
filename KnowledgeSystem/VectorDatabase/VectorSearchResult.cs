namespace KnowledgeSystem.VectorDatabase;

/// <summary>
///     Result for the vector query.
/// </summary>
/// <param name="index">The index of the vector in the database.</param>
/// <param name="score">The match score (based on the objective function).</param>
public readonly struct VectorSearchResult(int index, float score)
{
    public readonly int Index = index;
    public readonly float Score = score;
}