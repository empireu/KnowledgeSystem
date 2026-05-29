using KnowledgeSystem.Embedding;
using KnowledgeSystem.Vector.Hnsw;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Plugins.Wiki.Memory;

public sealed class MemoryStoreService(
    ILogger<MemoryStoreService> logger,
    IOptions<WikiOptions> options,
    IEmbeddingService embeddingService
) : IHostedService
{
    private sealed class Store
    {
        public required MemoryDbContext Db { get; init; }
        
        public required string HnswPath { get; init; }
        
        public required MutableHnswIndex Hnsw { get; init; }
        
        public readonly SemaphoreSlim DbSemaphore = new(1, 1);
    }

    private Store? _store;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var wikiConfig = options.Value;
        
        var dbContextOptions = new DbContextOptionsBuilder<MemoryDbContext>()
            .UseSqlite($"Data Source={wikiConfig.RepoPath}_memory.db")
            .Options;

        var hnswPath = $"{wikiConfig.RepoPath}_vector.hnsw";
        
        var db = new MemoryDbContext(dbContextOptions);
        
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var hnsw = File.Exists(hnswPath)
            ? MutableHnswIndex.LoadFromFile(hnswPath)
            : new MutableHnswIndex(embeddingService.Dimension, 16, 32);

        var hnswVectors = new HashSet<int>();
        for (var index = 0; index < hnsw.Vectors.Count; index++)
        {
            var storedVector = hnsw.Vectors[index];

            if (storedVector != null)
            {
                hnswVectors.Add(storedVector.Index);
            }
        }

        var dbVectors = await db.Memories
            .Select(x => x.Id)
            .ToListAsync(cancellationToken: cancellationToken);

        if (dbVectors.Count != hnswVectors.Count)
        {
            throw new Exception("Vectors count between database and vector index does not match");
        }
        
        for (var index = 0; index < dbVectors.Count; index++)
        {
            var vectorId = dbVectors[index];

            if (!hnswVectors.Contains(vectorId))
            {
                throw new Exception($"Vector {vectorId} from DB not found in HNSW");
            }
        }

        _store = new Store
        {
            Db = db,
            HnswPath = hnswPath,
            Hnsw = hnsw
        };
        
        logger.LogInformation("Loaded {count} memories",  dbVectors.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
    
    public async Task<int> CreateMemory(string summary, string content, DateTime utcCreatedAt, CancellationToken cancellationToken)
    {
        if (_store == null)
        {
            throw new InvalidOperationException("Memory store not initialized");
        }
        
        await _store.DbSemaphore.WaitAsync(cancellationToken);

        try
        {
            var embedding = await embeddingService.EmbedAsync(summary, cancellationToken);
        
            // Critical region with no cancellation:
            var vector = _store.Hnsw.Insert(embedding.ToArray());
            await _store.Db.Memories.AddAsync(new MemoryRecord
            {
                Id = vector.Index,
                Summary = summary,
                Content = content,
                UtcCreatedAt = utcCreatedAt
            }, CancellationToken.None);

            await _store.Db.SaveChangesAsync(CancellationToken.None);
        
            await using (var fs = File.Open(_store.HnswPath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                _store.Hnsw.SaveToFile(fs);
            }
            
            logger.LogInformation(
                "Creating memory {index}: {summary}: {content}",
                vector.Index, 
                summary,
                content
            );
        
            return vector.Index;
        }
        finally
        {
            _store.DbSemaphore.Release();
        }
    }

    public async Task<MemoryRecord?> GetMemoryAsync(int id, CancellationToken cancellationToken)
    {
        if (_store == null)
        {
            throw new InvalidOperationException("Memory store not initialized");
        }

        await _store.DbSemaphore.WaitAsync(cancellationToken);

        try
        {
            return await _store.Db.Memories.FirstOrDefaultAsync(x => x.Id == id, cancellationToken: cancellationToken);
        }
        finally
        {
            _store.DbSemaphore.Release();
        }
    }

    public async Task<MemoryRecord[]> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (_store == null)
        {
            throw new InvalidOperationException("Memory store not initialized");
        }
        
        var embedding = await embeddingService.EmbedAsync(query, cancellationToken);

        await _store.DbSemaphore.WaitAsync(cancellationToken);

        try
        {
            var topResults = _store.Hnsw.Search(
                embedding.Span, 
                5,
                1000
            );

            if (topResults.Length == 0)
            {
                return [];
            }

            var ids = topResults
                .Select(x => x.Index)
                .ToHashSet();

            return await _store.Db.Memories
                .Where(x => ids.Contains(x.Id))
                .ToArrayAsync(cancellationToken: cancellationToken);
        }
        finally
        {
            _store.DbSemaphore.Release();
        }
    }
}