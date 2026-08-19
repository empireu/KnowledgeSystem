using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Plugins.OreDb;

public sealed record OreQueryResult(string InstanceName, string Ore, int DepositCount, double MaxVolume);

public sealed record OrePopResult(string AsteroidName, double X, double Y, double Z, float Size, double TotalVolume, double Distance);

public sealed class OreDbStores(ILogger<OreDbStores> logger, IOptions<OreDbOptions> options ) : IHostedService
{
    private readonly SemaphoreSlim _dbSemaphore = new(1, 1);

    private OreDbDbContext? _db;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var dbContextOptions = new DbContextOptionsBuilder<OreDbDbContext>()
            .UseSqlite($"Data Source={options.Value.DatabasePath}")
            .Options;

        var db = new OreDbDbContext(dbContextOptions);

        await db.Database.EnsureCreatedAsync(cancellationToken);

        _db = db;

        logger.LogInformation("OreDb ready at {path}", options.Value.DatabasePath);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Replaces all asteroid data for the named instance with the given snapshot.
    ///     The instance is created if it does not exist yet.
    /// </summary>
    public async Task<int> ImportInstanceAsync(string instanceName, IReadOnlyList<ImportedAsteroid> asteroids, CancellationToken cancellationToken)
    {
        var db = GetDb();

        await _dbSemaphore.WaitAsync(cancellationToken);

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var gameInstance = await db.GameInstances
                .FirstOrDefaultAsync(i => i.Name == instanceName, cancellationToken);

            if (gameInstance == null)
            {
                gameInstance = new GameInstance { Name = instanceName };
                db.GameInstances.Add(gameInstance);
            }

            db.Asteroids.RemoveRange(db.Asteroids.Where(a => a.GameInstanceId == gameInstance.Id));

            foreach (var imported in asteroids)
            {
                var asteroid = new Asteroid
                {
                    GameInstance = gameInstance,
                    Name = imported.Name,
                    X = imported.X,
                    Y = imported.Y,
                    Z = imported.Z,
                    Size = imported.Size
                };

                foreach (var ore in imported.OreDeposits)
                {
                    asteroid.OreDeposits.Add(new OreDeposit
                    {
                        OreType = ore.OreType,
                        Volume = ore.Volume,
                        X = ore.X,
                        Y = ore.Y,
                        Z = ore.Z,
                        IsEstimated = ore.IsEstimated
                    });
                }

                db.Asteroids.Add(asteroid);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Imported {count} asteroids for instance {instance}", asteroids.Count, instanceName);

            return asteroids.Count;
        }
        finally
        {
            _dbSemaphore.Release();
        }
    }

    /// <summary>
    ///     Counts non-estimated deposits of the ore in the instance and returns the largest
    ///     deposit volume. Returns null if the instance does not exist.
    /// </summary>
    public async Task<OreQueryResult?> QueryAsync(string instanceName, string ore, CancellationToken cancellationToken)
    {
        var db = GetDb();

        await _dbSemaphore.WaitAsync(cancellationToken);

        try
        {
            var gameInstance = await db.GameInstances
                .FirstOrDefaultAsync(i => i.Name == instanceName, cancellationToken);

            if (gameInstance == null)
            {
                return null;
            }

            var deposits = db.OreDeposits.Where(d =>
                d.Asteroid.GameInstanceId == gameInstance.Id
                && d.OreType == ore
                && !d.IsEstimated
            );

            var count = await deposits.CountAsync(cancellationToken);
            var maxVolume = await deposits.MaxAsync(d => (double?)d.Volume, cancellationToken) ?? 0.0;

            return new OreQueryResult(instanceName, ore, count, maxVolume);
        }
        finally
        {
            _dbSemaphore.Release();
        }
    }

    /// <summary>
    ///     Returns the asteroid in the instance nearest to the given coordinates whose total non-estimated volume of the ore exceeds the minimum.
    ///     Returns null if the instance does not exist or no asteroid qualifies.
    /// </summary>
    public async Task<OrePopResult?> PopAsync(string instanceName, double x, double y, double z, string ore, double minVolume, CancellationToken cancellationToken)
    {
        var db = GetDb();

        await _dbSemaphore.WaitAsync(cancellationToken);

        try
        {
            var gameInstance = await db.GameInstances
                .FirstOrDefaultAsync(i => i.Name == instanceName, cancellationToken);

            if (gameInstance == null)
            {
                return null;
            }

            var candidates = await db.Asteroids
                .Where(a =>
                    a.GameInstanceId == gameInstance.Id
                    && a.OreDeposits.Any(d => d.OreType == ore && !d.IsEstimated))
                .Select(a => new
                {
                    a.Name,
                    a.X,
                    a.Y,
                    a.Z,
                    a.Size,
                    TotalVolume = a.OreDeposits
                        .Where(d => d.OreType == ore && !d.IsEstimated)
                        .Sum(d => d.Volume)
                })
                .ToListAsync(cancellationToken);

            OrePopResult? best = null;
            var bestSquaredDistance = double.MaxValue;

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                if (candidate.TotalVolume <= minVolume)
                {
                    continue;
                }

                var dx = candidate.X - x;
                var dy = candidate.Y - y;
                var dz = candidate.Z - z;

                var squaredDistance = dx * dx + dy * dy + dz * dz;

                if (squaredDistance >= bestSquaredDistance)
                {
                    continue;
                }

                bestSquaredDistance = squaredDistance;
                best = new OrePopResult(
                    candidate.Name,
                    candidate.X,
                    candidate.Y,
                    candidate.Z,
                    candidate.Size,
                    candidate.TotalVolume,
                    Math.Sqrt(squaredDistance));
            }

            return best;
        }
        finally
        {
            _dbSemaphore.Release();
        }
    }

    /// <summary>
    ///     Names of all imported instances.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListInstancesAsync(CancellationToken cancellationToken)
    {
        var db = GetDb();

        await _dbSemaphore.WaitAsync(cancellationToken);

        try
        {
            return await db.GameInstances
                .OrderBy(i => i.Name)
                .Select(i => i.Name)
                .ToListAsync(cancellationToken);
        }
        finally
        {
            _dbSemaphore.Release();
        }
    }

    private OreDbDbContext GetDb()
    {
        return _db ?? throw new InvalidOperationException("OreDb store not initialized");
    }
}
