using System.ComponentModel.DataAnnotations;

namespace KnowledgeSystem.Discord;

public class ReviewOptions
{
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
}