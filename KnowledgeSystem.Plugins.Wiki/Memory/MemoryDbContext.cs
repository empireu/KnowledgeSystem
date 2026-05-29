using Microsoft.EntityFrameworkCore;

namespace KnowledgeSystem.Plugins.Wiki.Memory;

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
                .ValueGeneratedNever();
            
            entity.Property(m => m.Summary)
                .IsRequired()
                .HasMaxLength(512);
            
            entity.Property(m => m.Content)
                .IsRequired()
                .HasMaxLength(16 * 1024);
        });
    }
}