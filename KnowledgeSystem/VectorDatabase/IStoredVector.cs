namespace KnowledgeSystem.VectorDatabase;

public interface IStoredVector
{
    int Index { get; }
        
    ReadOnlySpan<float> VectorView { get; }
        
    int Dimension => VectorView.Length;
}