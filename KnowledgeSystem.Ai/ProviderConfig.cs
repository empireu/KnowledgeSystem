using System.ComponentModel.DataAnnotations;

namespace KnowledgeSystem.Ai;

/// <summary>
///     Base config for the LLM API.
/// </summary>
public class ProviderConfig
{
    [Required]
    public string Endpoint { get; set; } = null!;

    [Required]
    public string Model { get; set; } = null!;

    [Required]
    public string Key { get; set; } = null!;

    public ProviderType ProviderType { get; set; } = ProviderType.Usual;
}