using KnowledgeSystem.Ai;

namespace KnowledgeSystem;

public class KnowledgeSystemConfig
{
    public const string Section = "core";
    
    public ProviderConfig? EmbeddingProvider { get; set; }
    
    public EmbeddingConfig? EmbeddingConfig { get; set; }
}