// ReSharper disable PropertyCanBeMadeInitOnly.Global

using System.ComponentModel.DataAnnotations;

namespace KnowledgeSystem.Retrieval;

/// <summary>
///     Configuration options for the RAG system, bound from IConfiguration.
/// </summary>
public class RagOptions
{
    public const string Section = "rag";

    [Required]
    public string RepositoryPath { get; set; } = null!;
    
    [Required]
    public string DatabasePath { get; set; } = null!;
    
    [Required]
    public string HnswIndexPath { get; set; } = null!;
    
    [Required]
    public string EmbeddingEndpoint { get; set; } = null!;
    
    [Required]
    public string EmbeddingModel { get; set; } = null!;
    
    [Required]    
    public string EmbeddingApiKey { get; set; } = null!;
    
    [Required]
    [Range(64, 2048, ErrorMessage = $"Invalid {nameof(EmbeddingDimension)}")]
    public int EmbeddingDimension { get; set; }
    
    [Required]
    [Range(32, 2048, ErrorMessage = $"Invalid {nameof(MaxChunkLength)}")]
    public int MaxChunkLength { get; set; }

    /// <summary>
    ///     String prepended to each request.
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    [Range(1, 16)]
    public int MaxReadTasks { get; set; } = 8;
    
    [Range(4, 128)]
    public int MaxConnectionsLane { get; set; } = 16;
    
    [Range(4, 128)]
    public int MaxConnectionsDense { get; set; } = 32;

    [Range(64, 2048)]
    public int EfConstruction { get; set; } = 200;
}
