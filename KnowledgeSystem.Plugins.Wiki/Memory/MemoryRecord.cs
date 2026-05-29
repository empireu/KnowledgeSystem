namespace KnowledgeSystem.Plugins.Wiki.Memory;

public class MemoryRecord
{
    public int Id { get; set; }

    public string Summary { get; set; } = null!;

    public string Content { get; set; } = null!;
    
    public DateTime UtcCreatedAt { get; set; }
}