using Microsoft.EntityFrameworkCore;

namespace KnowledgeSystem.Plugins.Surveillance.Database;

public sealed class IngestionDbContext(DbContextOptions<IngestionDbContext> options) : DbContext(options)
{
    public DbSet<IngestionBatch> Batches => Set<IngestionBatch>();
    public DbSet<IngestionMessage> Messages => Set<IngestionMessage>();
    public DbSet<IngestionChunk> Chunks => Set<IngestionChunk>();
    public DbSet<ExtractedEntityRecord> Entities => Set<ExtractedEntityRecord>();
    public DbSet<ExtractedClaimRecord> Claims => Set<ExtractedClaimRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IngestionBatch>(batch =>
        {
            batch.HasKey(b => b.Id);

            batch.Property(b => b.GuildName).IsRequired().HasMaxLength(256);
            batch.Property(b => b.ChannelName).IsRequired().HasMaxLength(256);

            batch.HasMany(b => b.Messages)
                .WithOne(m => m.Batch)
                .HasForeignKey(m => m.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            batch.HasMany(b => b.Chunks)
                .WithOne(c => c.Batch)
                .HasForeignKey(c => c.BatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IngestionMessage>(message =>
        {
            message.HasKey(m => m.Id);

            message.Property(m => m.Username).IsRequired().HasMaxLength(256);
            message.Property(m => m.Nickname).IsRequired().HasMaxLength(256);
            message.Property(m => m.Content).IsRequired().HasMaxLength(8192);
        });

        modelBuilder.Entity<IngestionChunk>(chunk =>
        {
            chunk.HasKey(c => c.Id);

            chunk.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);

            chunk.HasMany(c => c.Entities)
                .WithOne(e => e.Chunk)
                .HasForeignKey(e => e.ChunkId)
                .OnDelete(DeleteBehavior.Cascade);

            chunk.HasMany(c => c.Claims)
                .WithOne(c => c.Chunk)
                .HasForeignKey(c => c.ChunkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExtractedEntityRecord>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name).IsRequired().HasMaxLength(512);
            entity.Property(e => e.AliasesJson).IsRequired().HasMaxLength(8192);
            entity.Property(e => e.Type).HasMaxLength(256);
            entity.Property(e => e.Description).HasMaxLength(4096);
            entity.Property(e => e.EvidenceText).IsRequired().HasMaxLength(4096);

            entity.HasOne(e => e.SourceMessage)
                .WithMany()
                .HasForeignKey(e => e.SourceMessageId)
                .OnDelete(DeleteBehavior.ClientCascade);
        });

        modelBuilder.Entity<ExtractedClaimRecord>(claim =>
        {
            claim.HasKey(c => c.Id);

            claim.Property(c => c.SubjectName).IsRequired().HasMaxLength(512);
            claim.Property(c => c.Predicate).IsRequired().HasMaxLength(512);
            claim.Property(c => c.ObjectEntityName).HasMaxLength(512);
            claim.Property(c => c.ObjectLiteral).HasMaxLength(4096);
            claim.Property(c => c.Modality).IsRequired().HasMaxLength(128);
            claim.Property(c => c.EvidenceText).IsRequired().HasMaxLength(4096);

            claim.HasOne(c => c.SourceMessage)
                .WithMany()
                .HasForeignKey(c => c.SourceMessageId)
                .OnDelete(DeleteBehavior.ClientCascade);
        });
    }
}
