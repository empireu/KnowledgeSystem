using System.Numerics.Tensors;
using System.Runtime.CompilerServices;

namespace KnowledgeSystem.VectorDatabase;

public static class VectorObjective
{
    /// <summary>
    ///     "Distance" function using Cosine Similarity. Strictly speaking, it's not actually a distance or metric.
    /// </summary>
    /// <returns>A value in the range <c>[-1, 1]</c>. A lower value indicates the vectors are closer (for algorithm sorting).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float AdjustedCosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b) => 1.0f - TensorPrimitives.CosineSimilarity(a, b);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float AdjustedCosineSimilarity(IStoredVector a, IStoredVector b) => AdjustedCosineSimilarity(a.StorageView, b.StorageView);
}