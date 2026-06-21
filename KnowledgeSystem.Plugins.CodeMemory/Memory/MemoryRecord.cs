namespace KnowledgeSystem.Plugins.CodeMemory.Memory;

public class MemoryRecord
{
    public int Id { get; set; }

    // ReSharper disable once PropertyCanBeMadeInitOnly.Global
    public string Namespace { get; set; } = null!;
    
    public string Summary { get; set; } = null!;

    public byte[] SummaryEmbedding { get; set; } = null!;
    
    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public string Content { get; set; } = null!;
    
    public DateTime UtcCreatedAt { get; set; }

    public unsafe void GetEmbedding(Span<float> buffer)
    {
        fixed (byte* src = &SummaryEmbedding[0])
        fixed (float* dst = &buffer[0])
        {
            Buffer.MemoryCopy(
                src,
                dst,
                buffer.Length * sizeof(float),
                SummaryEmbedding.Length
            );
        }
    }

    public unsafe void LoadEmbedding(ReadOnlySpan<float> buffer)
    {
        if (SummaryEmbedding != null)
        {
            throw new InvalidOperationException("Cannot load embedding on non-fresh record");
        }

        SummaryEmbedding = new byte[buffer.Length * sizeof(float)];
        
        fixed (float* src = &buffer[0])
        fixed (byte* dst = &SummaryEmbedding[0])
        {
            Buffer.MemoryCopy(
                src,
                dst,
                SummaryEmbedding.Length,
                buffer.Length * sizeof(float)
            );
        }
    }
}