using KnowledgeSystem.Ai;

// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace KnowledgeSystem;

public class KnowledgeSystemConfig
{
    public const string Section = "core";
    
    public ProviderConfig? EmbeddingProvider { get; set; }
    
    public EmbeddingConfig? EmbeddingConfig { get; set; }
    
    public ProviderConfig? RerankingProvider { get; set; }
    
    public ProviderConfig? LogprobeFilteringProvider { get; set; }
}