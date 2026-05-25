using Microsoft.EntityFrameworkCore;

namespace KnowledgeSystem.Retrieval.Persistent;

/// <summary>
///     Database context for the RAG system.
///     Tracks known documents and their chunk-to-HNSW mappings for sync diffing.
/// </summary>
public class RagDbContext : DbContext
{
    public DbSet<DocumentRecord> Documents => Set<DocumentRecord>();
    public DbSet<ChunkRecord> Chunks => Set<ChunkRecord>();

    public RagDbContext(DbContextOptions<RagDbContext> options) : base(options)
    {
        
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentRecord>(entity =>
        {
            entity.HasKey(d => d.Path);
            entity.Property(d => d.Path).IsRequired();
        });

        modelBuilder.Entity<ChunkRecord>(entity =>
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
