using System.ComponentModel.DataAnnotations;
using KnowledgeSystem.Agents.Context.TokenEstimation;

namespace KnowledgeSystem.Agent.Config;

public class ChatOptions
{
    public const string Section = "chat";

    [Required]
    public string Endpoint { get; set; } = null!;

    [Required]
    public string ApiKey { get; set; } = null!;

    [Required]
    public string Model { get; set; } = null!;

    public string? ProviderOnly { get; set; } = null;

    public float? Temperature { get; set; } = 0.1f;
    
    [Required]
    public string SystemPromptFile { get; set; } = null!;
    
    [Required]
    public ChatTemplateFormat Template { get; set; }

    [Required] 
    public string TokenizerDir { get; set; } = null!;
    
    public bool Verbose { get; set; } = false;

    public ReviewOptions? Review { get; set; } = null!;
}
