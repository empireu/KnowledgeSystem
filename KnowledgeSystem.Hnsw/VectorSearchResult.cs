namespace KnowledgeSystem.Hnsw;

/// <summary>
///     Result for the vector query.
/// </summary>
/// <param name="index">The index of the vector in the database.</param>
/// <param name="score">The match score (based on the objective function).</param>
public readonly struct VectorSearchResult(int index, float score) : IEquatable<VectorSearchResult>
{
    public readonly int Index = index;
    public readonly float Score = score;

    public bool Equals(VectorSearchResult other)
    {
        return Index == other.Index;
    }

    public override bool Equals(object? obj)
    {
        return obj is VectorSearchResult other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Index;
    }

    public static bool operator ==(VectorSearchResult left, VectorSearchResult right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(VectorSearchResult left, VectorSearchResult right)
    {
        return !left.Equals(right);
    }
}