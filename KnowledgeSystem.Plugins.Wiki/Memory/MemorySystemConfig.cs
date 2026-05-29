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
}