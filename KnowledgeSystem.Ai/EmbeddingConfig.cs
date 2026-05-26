using System.ComponentModel.DataAnnotations;

// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace KnowledgeSystem.Ai;

/// <summary>
///     Configuration for an embedding service, extending the base provider config with embedding-specific parameters.
///     P.S. An embedding service is supposed to be used with <see cref="ProviderType.Usual"/>!
/// </summary>
public class EmbeddingConfig : ProviderConfig
{
    [Required]
    [Range(64, 2048, ErrorMessage = $"Invalid {nameof(Dimension)}")]
    public int Dimension { get; set; }

    /// <summary>
    ///     String prepended to each embedding request.
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;
}
