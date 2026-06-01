// ReSharper disable PropertyCanBeMadeInitOnly.Global

using System.ComponentModel.DataAnnotations;
using KnowledgeSystem.Ai;

namespace KnowledgeSystem.Retrieval.Persistent.Document;

/// <summary>
///     Configuration options for the RAG system, bound from IConfiguration.
/// </summary>
public class WikiDiskMarkdownStoreDescription
{
    [Required]
    public string RepositoryPath { get; set; } = null!;

    /// <summary>
    ///     Unique ID for this store.
    /// </summary>
    public string StoreId { get; set; } = "wiki";
    
    [Required]
    public string DatabasePath { get; set; } = null!;
    
    [Required]
    public string HnswIndexPath { get; set; } = null!;

    /// <summary>
    ///     Embedding service configuration. Bound from the "rag:embedding" config section.
    /// </summary>
    [Required]
    public EmbeddingConfig Embedding { get; set; } = null!;
    
    [Required]
    [Range(32, 2048, ErrorMessage = $"Invalid {nameof(MaxChunkLength)}")]
    public int MaxChunkLength { get; set; }

    [Range(1, 16)]
    public int MaxReadTasks { get; set; } = 8;
    
    [Range(4, 128)]
    public int MaxConnectionsLane { get; set; } = 16;
    
    [Range(4, 128)]
    public int MaxConnectionsDense { get; set; } = 32;

    [Range(64, 2048)]
    public int EfConstruction { get; set; } = 200;
    
    [Range(1, 10)]
    public int ParallelInsert { get; set; } = 8;
    
    [Range(1, 100)]
    public int EmbeddingBatchSize { get; set; } = 64;
}
