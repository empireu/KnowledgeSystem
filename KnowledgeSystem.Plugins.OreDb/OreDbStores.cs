using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord.Services.ApplicationCommands;
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Plugins.OreDb;

public enum PopMode
{
    [SlashCommandChoice(Name = "Sum")]
    Sum = 0,

    [SlashCommandChoice(Name = "Deposit")]
    Deposit = 1
}

// ReSharper disable NotAccessedPositionalProperty.Global

public sealed record OreQueryResult(string InstanceName, string Ore, int DepositCount, double LargestDeposit, double LargestSum, string? RichestAsteroidName = null);

public sealed record OreVolume(string OreType, double Volume);

public sealed record OrePopResult(string AsteroidName, double X, double Y, double Z, float Size, double Volume, double Distance, IReadOnlyList<OreVolume> Ores);

public sealed record OreAsteroidResult(string AsteroidName, double X, double Y, double Z, float Size, double Volume, double? Distance, IReadOnlyList<OreVolume> Ores);

// ReSharper restore NotAccessedPositionalProperty.Global

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
    ///     Replaces all asteroid data for the named instance with the given data.
    ///     The instance is created if it does not exist yet.
    /// </summary>
    public async Task<int> ImportInstanceAsync(string instanceName, ChannelReader<ImportedAsteroid> reader, CancellationToken cancellationToken)
    {
        instanceName = instanceName.ToLowerInvariant();

        await _dbSemaphore.WaitAsync(cancellationToken);

        try
        {
            await using var connection = new SqliteConnection($"Data Source={options.Value.DatabasePath};Foreign Keys=True");
            await connection.OpenAsync(cancellationToken);

            await using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
                await pragma.ExecuteNonQueryAsync(cancellationToken);
            }

            var gameInstanceId = await GetOrCreateInstanceAsync(connection, instanceName, cancellationToken);

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM Asteroids WHERE GameInstanceId = @gi;";
                delete.Parameters.Add(new SqliteParameter("@gi", gameInstanceId));
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var drop = connection.CreateCommand())
            {
                drop.Transaction = transaction;
                drop.CommandText = DropIndexesSql;
                await drop.ExecuteNonQueryAsync(cancellationToken);
            }

            var count = 0;

            await using (var insertAsteroid = connection.CreateCommand())
            await using (var insertDeposit = connection.CreateCommand())
            {
                insertAsteroid.Transaction = transaction;
                insertAsteroid.CommandText = "INSERT INTO Asteroids (GameInstanceId, Name, X, Y, Z, Size, IsMined) VALUES (@gi, @name, @x, @y, @z, @size, 0) RETURNING Id;";
                insertAsteroid.Parameters.Add(new SqliteParameter("@gi", gameInstanceId));
                var name = insertAsteroid.Parameters.Add(new SqliteParameter("@name", ""));
                var x = insertAsteroid.Parameters.Add(new SqliteParameter("@x", 0.0));
                var y = insertAsteroid.Parameters.Add(new SqliteParameter("@y", 0.0));
                var z = insertAsteroid.Parameters.Add(new SqliteParameter("@z", 0.0));
                var size = insertAsteroid.Parameters.Add(new SqliteParameter("@size", 0f));

                insertDeposit.Transaction = transaction;
                insertDeposit.CommandText = "INSERT INTO OreDeposits (AsteroidId, GameInstanceId, OreType, Volume, X, Y, Z, IsEstimated) VALUES (@aid, @gi, @ore, @vol, @dx, @dy, @dz, @est);";
                var asteroidId = insertDeposit.Parameters.Add(new SqliteParameter("@aid", 0L));
                insertDeposit.Parameters.Add(new SqliteParameter("@gi", gameInstanceId));
                var ore = insertDeposit.Parameters.Add(new SqliteParameter("@ore", ""));
                var volume = insertDeposit.Parameters.Add(new SqliteParameter("@vol", 0.0));
                var dx = insertDeposit.Parameters.Add(new SqliteParameter("@dx", 0.0));
                var dy = insertDeposit.Parameters.Add(new SqliteParameter("@dy", 0.0));
                var dz = insertDeposit.Parameters.Add(new SqliteParameter("@dz", 0.0));
                var estimated = insertDeposit.Parameters.Add(new SqliteParameter("@est", 0L));

                await foreach (var imported in reader.ReadAllAsync(cancellationToken))
                {
                    name.Value = imported.Name;
                    x.Value = imported.X;
                    y.Value = imported.Y;
                    z.Value = imported.Z;
                    size.Value = imported.Size;

                    var id = Convert.ToInt64(await insertAsteroid.ExecuteScalarAsync(cancellationToken));

                    for (var index = 0; index < imported.OreDeposits.Count; index++)
                    {
                        var deposit = imported.OreDeposits[index];
                        asteroidId.Value = id;
                        ore.Value = deposit.OreType.ToLowerInvariant();
                        volume.Value = deposit.Volume;
                        dx.Value = deposit.X;
                        dy.Value = deposit.Y;
                        dz.Value = deposit.Z;
                        estimated.Value = deposit.IsEstimated ? 1L : 0L;
                        await insertDeposit.ExecuteNonQueryAsync(cancellationToken);
                    }

                    count++;
                }
            }

            await using (var create = connection.CreateCommand())
            {
                create.Transaction = transaction;
                create.CommandText = CreateIndexesSql;
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Imported {count} asteroids for instance {instance}", count, instanceName);

            return count;
        }
        finally
        {
            _dbSemaphore.Release();
        }
    }

    private static async Task<int> GetOrCreateInstanceAsync(SqliteConnection connection, string instanceName, CancellationToken cancellationToken)
    {
        await using (var find = connection.CreateCommand())
        {
            find.CommandText = "SELECT Id FROM GameInstances WHERE Name = @name;";
            find.Parameters.Add(new SqliteParameter("@name", instanceName));
            var existing = await find.ExecuteScalarAsync(cancellationToken);
            if (existing != null)
            {
                return Convert.ToInt32(existing);
            }
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO GameInstances (Name) VALUES (@name) RETURNING Id;";
            insert.Parameters.Add(new SqliteParameter("@name", instanceName));
            return Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));
        }
    }

    private const string DropIndexesSql =
        "DROP INDEX IF EXISTS IX_Asteroids_GameInstanceId_Name; " +
        "DROP INDEX IF EXISTS IX_Asteroids_GameInstanceId; " +
        "DROP INDEX IF EXISTS IX_OreDeposits_AsteroidId; " +
        "DROP INDEX IF EXISTS IX_OreDeposits_OreType; " +
        "DROP INDEX IF EXISTS IX_OreDeposits_GameInstanceId_OreType_IsEstimated; " +
        "DROP INDEX IF EXISTS IX_OreDeposits_AsteroidId_OreType_IsEstimated;";

    private const string CreateIndexesSql =
        "CREATE UNIQUE INDEX IF NOT EXISTS IX_Asteroids_GameInstanceId_Name ON Asteroids(GameInstanceId, Name); " +
        "CREATE INDEX IF NOT EXISTS IX_Asteroids_GameInstanceId ON Asteroids(GameInstanceId); " +
        "CREATE INDEX IF NOT EXISTS IX_OreDeposits_AsteroidId ON OreDeposits(AsteroidId); " +
        "CREATE INDEX IF NOT EXISTS IX_OreDeposits_OreType ON OreDeposits(OreType); " +
        "CREATE INDEX IF NOT EXISTS IX_OreDeposits_GameInstanceId_OreType_IsEstimated ON OreDeposits(GameInstanceId, OreType, IsEstimated); " +
        "CREATE INDEX IF NOT EXISTS IX_OreDeposits_AsteroidId_OreType_IsEstimated ON OreDeposits(AsteroidId, OreType, IsEstimated);";

    #region CLI
    
    /// <summary>
    ///     Counts non-estimated deposits of the ore on unmined asteroids in the instance and returns the largest single deposit and the largest asteroid total.
    ///     Returns null if the instance does not exist.
    /// </summary>
    public async Task<OreQueryResult?> QueryAsync(string instanceName, string ore, CancellationToken cancellationToken)
    {
        var db = GetDb();

        await _dbSemaphore.WaitAsync(cancellationToken);

        try
        {
            var gameInstance = await db.GameInstances.FirstOrDefaultAsync(i => i.Name == instanceName, cancellationToken);

            if (gameInstance == null)
            {
                return null;
            }

            var deposits = db.OreDeposits
                .Where(d => d.GameInstanceId == gameInstance.Id && !d.Asteroid.IsMined && d.OreType == ore && !d.IsEstimated);

            var count = await deposits.CountAsync(cancellationToken);
            var largestDeposit = await deposits.MaxAsync(d => (double?)d.Volume, cancellationToken) ?? 0.0;

            var richest = await db.Asteroids
                .Where(a =>
                    a.GameInstanceId == gameInstance.Id
                    && !a.IsMined
                    && a.OreDeposits.Any(d => d.OreType == ore && !d.IsEstimated))
                .Select(a => new
                {
                    a.Name,
                    Sum = a.OreDeposits
                        .Where(d => d.OreType == ore && !d.IsEstimated)
                        .Sum(d => d.Volume)
                })
                .OrderByDescending(a => a.Sum)
                .FirstOrDefaultAsync(cancellationToken);

            return new OreQueryResult(instanceName, ore, count, largestDeposit, richest?.Sum ?? 0.0, richest?.Name);
        }
        finally
        {
            _dbSemaphore.Release();
        }
    }

    /// <summary>
    ///     Returns the asteroid in the instance nearest to the given coordinates whose qualifying ore volume (total of all deposits, or largest single deposit in deposit mode) exceeds the minimum, and marks it as mined so later pops skip it.
    ///     Returns null if the instance does not exist or no asteroid qualifies.
    /// </summary>
    public async Task<OrePopResult?> PopAsync(string instanceName, double x, double y, double z, string ore, double minVolume, PopMode mode, CancellationToken cancellationToken)
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
                .Where(a => a.GameInstanceId == gameInstance.Id && !a.IsMined && a.OreDeposits.Any(d => d.OreType == ore && !d.IsEstimated))
                .Select(a => 
                    new
                    {
                        a.Id, a.Name, a.X, a.Y, a.Z, a.Size,
                        TotalVolume = a.OreDeposits
                            .Where(d => d.OreType == ore && !d.IsEstimated)
                            .Sum(d => d.Volume),
                        LargestDeposit = a.OreDeposits
                            .Where(d => d.OreType == ore && !d.IsEstimated)
                            .Max(d => (double?)d.Volume) ?? 0.0
                    }
                )
                .ToListAsync(cancellationToken);

            OrePopResult? best = null;
            var bestId = 0;
            var bestSquaredDistance = double.MaxValue;

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var measure = mode == PopMode.Sum ? candidate.TotalVolume : candidate.LargestDeposit;
                if (measure <= minVolume)
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
                bestId = candidate.Id;
                best = new OrePopResult(
                    candidate.Name,
                    candidate.X,
                    candidate.Y,
                    candidate.Z,
                    candidate.Size,
                    measure,
                    Math.Sqrt(squaredDistance),
                    []
                );
            }

            if (best != null)
            {
                var mined = await db.Asteroids.FindAsync([bestId], cancellationToken);
                
                if (mined != null)
                {
                    mined.IsMined = true;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            if (best != null && mode == PopMode.Deposit)
            {
                var deposit = await db.OreDeposits
                    .Where(d => d.AsteroidId == bestId && d.OreType == ore && !d.IsEstimated)
                    .OrderByDescending(d => d.Volume)
                    .Select(d => new { d.X, d.Y, d.Z })
                    .FirstOrDefaultAsync(cancellationToken);

                if (deposit != null)
                {
                    best = best with { X = deposit.X, Y = deposit.Y, Z = deposit.Z };
                }
            }

            if (best != null)
            {
                var ores = await db.OreDeposits
                    .Where(d => d.AsteroidId == bestId && !d.IsEstimated)
                    .GroupBy(d => d.OreType)
                    .Select(g => new { OreType = g.Key, Volume = g.Sum(d => d.Volume) })
                    .ToListAsync(cancellationToken);

                best = best with
                {
                    Ores = ores
                        .OrderByDescending(o => o.Volume)
                        .Select(o => new OreVolume(o.OreType, o.Volume))
                        .ToList()
                };
            }

            return best;
        }
        finally
        {
            _dbSemaphore.Release();
        }
    }

    #endregion

    #region Agent
    
    /// <summary>
    ///     Returns the asteroid in the instance nearest to the given coordinates whose total volume of the ore exceeds the minimum.
    ///     When markMined is true, marks it as mined so later queries skip it.
    ///     Returns null if the instance does not exist or no asteroid qualifies.
    /// </summary>
    public async Task<OreAsteroidResult?> ClosestOreAsync(string instanceName, double x, double y, double z, string ore, double minVolume, bool markMined, CancellationToken cancellationToken)
    {
        var db = GetDb();

        await _dbSemaphore.WaitAsync(cancellationToken);

        try
        {
            var gameInstance = await db.GameInstances.FirstOrDefaultAsync(i => i.Name == instanceName, cancellationToken);

            if (gameInstance == null)
            {
                return null;
            }

            var candidates = await LoadCandidatesAsync(db, gameInstance.Id, ore, cancellationToken);

            CandidateAsteroid? best = null;
            var bestSquaredDistance = double.MaxValue;

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];

                if (candidate.Total <= minVolume)
                {
                    continue;
                }

                var squaredDistance = SquaredDistance(candidate, x, y, z);

                if (squaredDistance >= bestSquaredDistance)
                {
                    continue;
                }

                bestSquaredDistance = squaredDistance;
                best = candidate;
            }

            if (best == null)
            {
                return null;
            }

            var result = await BuildResultAsync(db, best.Value, Math.Sqrt(bestSquaredDistance), cancellationToken);

            if (markMined)
            {
                await MarkMinedAsync(db, best.Value.Id, cancellationToken);
            }

            return result;
        }
        finally
        {
            _dbSemaphore.Release();
        }
    }

    /// <summary>
    ///     Returns the unmined asteroid in the instance with the largest total volume of the ore.
    ///     When an origin is given, only asteroids within the maximum distance qualify and the distance of the result is measured from that origin.
    ///     When markMined is true, marks the asteroid as mined so later queries skip it.
    ///     Returns null if the instance does not exist or no asteroid qualifies.
    /// </summary>
    public async Task<OreAsteroidResult?> LargestOreAsync(string instanceName, string ore, double? originX, double? originY, double? originZ, double? maxDistanceKm, bool markMined, CancellationToken cancellationToken)
    {
        var db = GetDb();

        await _dbSemaphore.WaitAsync(cancellationToken);

        try
        {
            var gameInstance = await db.GameInstances.FirstOrDefaultAsync(i => i.Name == instanceName, cancellationToken);

            if (gameInstance == null)
            {
                return null;
            }

            var candidates = await LoadCandidatesAsync(db, gameInstance.Id, ore, cancellationToken);

            var hasOrigin = originX != null && originY != null && originZ != null;
            double? maxDistanceMeters = maxDistanceKm == null ? null : maxDistanceKm.Value * 1000.0;

            CandidateAsteroid? best = null;
            var bestVolume = 0.0;
            var bestSquaredDistance = 0.0;

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];

                var squaredDistance = hasOrigin
                    ? SquaredDistance(candidate, originX!.Value, originY!.Value, originZ!.Value)
                    : 0.0;

                if (maxDistanceMeters != null && squaredDistance > maxDistanceMeters.Value * maxDistanceMeters.Value)
                {
                    continue;
                }

                if (candidate.Total <= bestVolume)
                {
                    continue;
                }

                bestVolume = candidate.Total;
                bestSquaredDistance = squaredDistance;
                best = candidate;
            }

            if (best == null)
            {
                return null;
            }

            double? distance = hasOrigin ? Math.Sqrt(bestSquaredDistance) : null;

            var result = await BuildResultAsync(db, best.Value, distance, cancellationToken);

            if (markMined)
            {
                await MarkMinedAsync(db, best.Value.Id, cancellationToken);
            }

            return result;
        }
        finally
        {
            _dbSemaphore.Release();
        }
    }

    /// <summary>
    ///     Returns up to <see cref="count"/> unmined asteroids in the instance nearest to the given coordinates whose total volume of the ore exceeds the minimum, ordered by distance.
    ///     Returns null if the instance does not exist, or an empty list if no asteroid qualifies.
    /// </summary>
    public async Task<IReadOnlyList<OreAsteroidResult>?> NearbyOresAsync(string instanceName, double x, double y, double z, string ore, double minVolume, int count, CancellationToken cancellationToken)
    {
        var db = GetDb();

        await _dbSemaphore.WaitAsync(cancellationToken);

        try
        {
            var gameInstance = await db.GameInstances.FirstOrDefaultAsync(i => i.Name == instanceName, cancellationToken);

            if (gameInstance == null)
            {
                return null;
            }

            var candidates = await LoadCandidatesAsync(db, gameInstance.Id, ore, cancellationToken);

            var nearest = new List<(CandidateAsteroid Candidate, double SquaredDistance)>();

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];

                if (candidate.Total <= minVolume)
                {
                    continue;
                }

                nearest.Add((candidate, SquaredDistance(candidate, x, y, z)));
            }

            nearest.Sort(static (left, right) => left.SquaredDistance.CompareTo(right.SquaredDistance));

            var results = new List<OreAsteroidResult>();

            for (var index = 0; index < Math.Min(count, nearest.Count); index++)
            {
                var entry = nearest[index];
                results.Add(await BuildResultAsync(db, entry.Candidate, Math.Sqrt(entry.SquaredDistance), cancellationToken));
            }

            return results;
        }
        finally
        {
            _dbSemaphore.Release();
        }
    }
    
    #endregion

    private record struct CandidateAsteroid(int Id, string Name, double X, double Y, double Z, float Size, double Total);

    private static async Task<List<CandidateAsteroid>> LoadCandidatesAsync(OreDbDbContext db, int gameInstanceId, string ore, CancellationToken cancellationToken)
    {
        var rows = await db.Asteroids
            .Where(a => a.GameInstanceId == gameInstanceId && !a.IsMined && a.OreDeposits.Any(d => d.OreType == ore && !d.IsEstimated))
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.X,
                a.Y,
                a.Z,
                a.Size,
                Total = a.OreDeposits
                    .Where(d => d.OreType == ore && !d.IsEstimated)
                    .Sum(d => d.Volume)
            })
            .ToListAsync(cancellationToken);

        var candidates = new List<CandidateAsteroid>(rows.Count);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            candidates.Add(new CandidateAsteroid(row.Id, row.Name, row.X, row.Y, row.Z, row.Size, row.Total));
        }

        return candidates;
    }

    private static double SquaredDistance(CandidateAsteroid asteroid, double x, double y, double z)
    {
        var dx = asteroid.X - x;
        var dy = asteroid.Y - y;
        var dz = asteroid.Z - z;

        return dx * dx + dy * dy + dz * dz;
    }

    private static async Task<OreAsteroidResult> BuildResultAsync(OreDbDbContext db, CandidateAsteroid asteroid, double? distance, CancellationToken cancellationToken)
    {
        var ores = await db.OreDeposits
            .Where(d => d.AsteroidId == asteroid.Id && !d.IsEstimated)
            .GroupBy(d => d.OreType)
            .Select(g => new { OreType = g.Key, Volume = g.Sum(d => d.Volume) })
            .ToListAsync(cancellationToken);

        return new OreAsteroidResult(
            asteroid.Name,
            asteroid.X,
            asteroid.Y,
            asteroid.Z,
            asteroid.Size,
            asteroid.Total,
            distance,
            ores
                .OrderByDescending(o => o.Volume)
                .Select(o => new OreVolume(o.OreType, o.Volume))
                .ToList()
        );
    }

    private static async Task MarkMinedAsync(OreDbDbContext db, int asteroidId, CancellationToken cancellationToken)
    {
        var mined = await db.Asteroids.FindAsync([asteroidId], cancellationToken);

        if (mined == null)
        {
            return;
        }

        mined.IsMined = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    private OreDbDbContext GetDb()
    {
        return _db ?? throw new InvalidOperationException("OreDb store not initialized");
    }
}
