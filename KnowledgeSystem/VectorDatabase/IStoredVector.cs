namespace KnowledgeSystem.VectorDatabase;

public interface IStoredVector
{
    int Index { get; }
        
    ReadOnlySpan<float> StorageView { get; }
        
    int Dimension => StorageView.Length;
}