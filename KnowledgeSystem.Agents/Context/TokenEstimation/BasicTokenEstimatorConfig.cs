using System.ComponentModel.DataAnnotations;

// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace KnowledgeSystem.Agents.Context.TokenEstimation;

public sealed class BasicTokenEstimatorConfig
{
    [Required] 
    public required string ModelName { get; set; } = null!;

    [Required] 
    public required TokenizerKind Kind { get; set; } = TokenizerKind.Invalid;

    [Required] 
    public required ChatTemplateFormat ChatFormat { get; init; } = ChatTemplateFormat.Invalid;
    
    public string? TokenizerDir { get; init; }
}