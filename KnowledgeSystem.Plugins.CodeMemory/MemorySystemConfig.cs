using System.ComponentModel.DataAnnotations;
using KnowledgeSystem.Ai;

namespace KnowledgeSystem.Plugins.CodeMemory;

public class MemorySystemConfig
{
    public const string Section = "code_memory";
    
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
    ///     The number of results to take from each search method.
    /// </summary>
    public int ResultsPerMethod { get; set; } = 20;

    /// <summary>
    ///     The number of results to take from RRF.
    /// </summary>
    public int FusedResults { get; set; } = 20;
    
    /// <summary>
    ///     The number of final results to take with reranking.
    /// </summary>
    public int TopN { get; set; } = 5;
    
    /// <summary>
    ///     Results are dropped if they are below this threshold.
    /// </summary>
    public double GateThreshold { get; set; } = 0.5;
}