using Microsoft.EntityFrameworkCore;

namespace KnowledgeSystem.Retrieval.Persistent.Document;

/// <summary>
///     Database context for the RAG system.
///     Tracks known documents and their chunk-to-HNSW mappings for sync diffing.
/// </summary>
public class DiskMarkdownDocumentStoreTrackerDbContext(DbContextOptions<DiskMarkdownDocumentStoreTrackerDbContext> options) : DbContext(options)
{
    public DbSet<MarkdownDocumentRecord> Documents => Set<MarkdownDocumentRecord>();
    public DbSet<MarkdownDocumentChunkRecord> Chunks => Set<MarkdownDocumentChunkRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MarkdownDocumentRecord>(entity =>
        {
            entity.HasKey(d => d.Path);
            entity.Property(d => d.Path).IsRequired();
        });

        modelBuilder.Entity<MarkdownDocumentChunkRecord>(entity =>
        {
            entity.HasKey(c => c.HashHex);
            entity.Property(c => c.HashHex).IsRequired();

            entity.HasOne(c => c.Document)
                .WithMany(d => d.Chunks)
                .HasForeignKey(c => c.DocumentPath)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(c => c.DocumentPath);
            entity.HasIndex(c => c.ChunkId);
        });
    }
}
