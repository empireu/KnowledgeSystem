using Microsoft.EntityFrameworkCore;

namespace KnowledgeSystem.Plugins.CodeMemory.Memory;

public class MemoryDbContext(DbContextOptions<MemoryDbContext> options) : DbContext(options)
{
    public DbSet<MemoryRecord> Memories => Set<MemoryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MemoryRecord>(entity =>
        {
            entity.HasKey(m => m.Id);

            entity.Property(m => m.Id)
                .IsRequired()
                .ValueGeneratedOnAdd();

            entity.Property(m => m.Namespace)
                .IsRequired()
                .HasMaxLength(128);

            entity.HasIndex(m => m.Namespace);
            
            entity.Property(m => m.Summary)
                .IsRequired()
                .HasMaxLength(4096);

            entity.Property(m => m.SummaryEmbedding)
                .IsRequired()
                .HasMaxLength(1024 * sizeof(float));

            entity.Property(m => m.Content)
                .IsRequired();
        });
    }
}