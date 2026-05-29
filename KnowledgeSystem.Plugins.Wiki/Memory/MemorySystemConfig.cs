using System.ComponentModel.DataAnnotations;
using KnowledgeSystem.Ai;

namespace KnowledgeSystem.Plugins.Wiki.Memory;

public class MemorySystemConfig
{
    [Required]
    public ProviderConfig ExtractionProvider { get; set; } = null!;
    
    public ChatOptionsConfig Extraction { get; set; } = new();

    [Required]
    public string ExtractionSystemPrompt { get; set; } = null!;
    
    /// <summary>
    ///     If true, the extraction agent will be given a single turn.
    /// </summary>
    public bool RunForOneTurn { get; set; } = false;
    
    /// <summary>
    ///     Reranked results are dropped if they are below this score.
    /// </summary>
    public float RerankingThreshold { get; set; } = 0.5f;
}