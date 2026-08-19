using Microsoft.EntityFrameworkCore;

namespace KnowledgeSystem.Plugins.OreDb;

public class OreDbDbContext(DbContextOptions<OreDbDbContext> options) : DbContext(options)
{
    public DbSet<GameInstance> GameInstances => Set<GameInstance>();

    public DbSet<Asteroid> Asteroids => Set<Asteroid>();

    public DbSet<OreDeposit> OreDeposits => Set<OreDeposit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameInstance>(entity =>
        {
            entity.HasKey(i => i.Id);

            entity.Property(i => i.Id)
                .IsRequired()
                .ValueGeneratedOnAdd();

            entity.Property(i => i.Name)
                .IsRequired()
                .HasMaxLength(128);

            entity.HasIndex(i => i.Name)
                .IsUnique();
        });

        modelBuilder.Entity<Asteroid>(entity =>
        {
            entity.HasKey(a => a.Id);

            entity.Property(a => a.Id)
                .IsRequired()
                .ValueGeneratedOnAdd();

            entity.Property(a => a.Name)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(a => a.IsMined)
                .IsRequired();

            entity.HasIndex(a => new { a.GameInstanceId, a.Name })
                .IsUnique();

            entity.HasOne(a => a.GameInstance)
                .WithMany(i => i.Asteroids)
                .HasForeignKey(a => a.GameInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OreDeposit>(entity =>
        {
            entity.HasKey(d => d.Id);

            entity.Property(d => d.Id)
                .IsRequired()
                .ValueGeneratedOnAdd();

            entity.Property(d => d.OreType)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(d => d.GameInstanceId)
                .IsRequired();

            entity.HasIndex(d => d.OreType);

            entity.HasIndex(d => new { d.GameInstanceId, d.OreType, d.IsEstimated });

            entity.HasIndex(d => new { d.AsteroidId, d.OreType, d.IsEstimated });

            entity.HasOne(d => d.Asteroid)
                .WithMany(a => a.OreDeposits)
                .HasForeignKey(d => d.AsteroidId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
