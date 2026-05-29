using KnowledgeSystem.Embedding;
using KnowledgeSystem.Reranking;
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
    IEmbeddingService embeddingService,
    IRerankingService rerankingService
) : IHostedService
{
    private sealed class Store
    {
        public required MemoryDbContext Db { get; init; }
        
        public required string HnswPath { get; init; }
        
        public required MutableHnswIndex Hnsw { get; init; }
        
        public required MemoryLexicalIndex LexicalIndex { get; init; }
        
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

        var lexicalIndex = new MemoryLexicalIndex();
        
        var allMemories = await db.Memories
            .Select(x => new { x.Id, x.Summary })
            .ToListAsync(cancellationToken: cancellationToken);
        
        lexicalIndex.Build(allMemories.Select(x => (x.Id, x.Summary)));

        _store = new Store
        {
            Db = db,
            HnswPath = hnswPath,
            Hnsw = hnsw,
            LexicalIndex = lexicalIndex
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
            
            _store.LexicalIndex.Add(vector.Index, summary);
        
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

    public async Task<bool> DeleteMemory(int id, CancellationToken cancellationToken)
    {
        if (_store == null)
        {
            throw new InvalidOperationException("Memory store not initialized");
        }
        
        await _store.DbSemaphore.WaitAsync(cancellationToken);

        try
        {
            var record = await _store.Db.Memories.FirstOrDefaultAsync(x => x.Id == id, cancellationToken: cancellationToken);
            
            if (record == null)
            {
                return false;
            }

            var storedVector = _store.Hnsw.Vectors[id];
            
            if (storedVector != null)
            {
                _store.Hnsw.Remove(storedVector);
                
                await using (var fs = File.Open(_store.HnswPath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                {
                    _store.Hnsw.SaveToFile(fs);
                }
            }

            _store.Db.Memories.Remove(record);
            await _store.Db.SaveChangesAsync(CancellationToken.None);

            _store.LexicalIndex.Remove(id, record.Summary);

            logger.LogInformation(
                "Deleted memory {index}: {summary}",
                id,
                record.Summary
            );

            return true;
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

    private const int HnswK = 20;
    private const int Bm25K = 20;
    private const int RrfK = 60;
    private const int RerankTopN = 5;

    public async Task<MemoryRecord[]> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (_store == null)
        {
            throw new InvalidOperationException("Memory store not initialized");
        }

        var embedding = await embeddingService.EmbedAsync(query, cancellationToken);
        
        MemoryRecord[] memories;
        await _store.DbSemaphore.WaitAsync(cancellationToken);
        try
        {
            var hnswResults = _store.Hnsw.Search(embedding.Span, HnswK, 1000);
            var bm25Results = _store.LexicalIndex.SearchBm25(query, Bm25K);

            if (hnswResults.Length == 0 && bm25Results.Length == 0)
            {
                return [];
            }

            // Build rank maps for RRF:
            var hnswRanks = new Dictionary<int, int>(hnswResults.Length);
            for (var i = 0; i < hnswResults.Length; i++)
            {
                hnswRanks[hnswResults[i].Index] = i + 1;
            }

            var bm25Ranks = new Dictionary<int, int>(bm25Results.Length);
            for (var i = 0; i < bm25Results.Length; i++)
            {
                bm25Ranks[bm25Results[i].ChunkId] = i + 1;
            }

            // RRF fusion across both result sets like the fast context:
            var rrfScores = new Dictionary<int, double>();
            foreach (var (id, rank) in hnswRanks)
            {
                rrfScores[id] = 1.0 / (RrfK + rank);
            }

            foreach (var (id, rank) in bm25Ranks)
            {
                var bm25Rrf = 1.0 / (RrfK + rank);
                if (rrfScores.TryGetValue(id, out var existing))
                {
                    rrfScores[id] = existing + bm25Rrf;
                }
                else
                {
                    rrfScores[id] = bm25Rrf;
                }
            }

            var candidateIds = rrfScores
                .OrderByDescending(x => x.Value)
                .Take(HnswK)
                .Select(x => x.Key)
                .ToHashSet();

            memories = await _store.Db.Memories
                .Where(x => candidateIds.Contains(x.Id))
                .ToArrayAsync(cancellationToken: cancellationToken);
        }
        finally
        {
            _store.DbSemaphore.Release();
        }

        if (memories.Length <= RerankTopN)
        {
            return memories;
        }

        var summaries = memories
            .Select(x => x.Summary)
            .ToList();
        
        var rerankResults = await rerankingService.RerankAsync(query, summaries, RerankTopN, cancellationToken);

        if (rerankResults == null)
        {
            logger.LogError("Reranking error");
            return [];
        }

        var threshold = options.Value.Memory!.RerankingThreshold;
        var passedResults = rerankResults
            .Where(x => x.RelevanceScore >= threshold)
            .ToArray();

        if (passedResults.Length == 0)
        {
            return [];
        }
        
        Console.WriteLine($"Q: {query}");
        Console.WriteLine($"Pass: {passedResults.Length}");
        foreach (var passedResult in passedResults)
        {
            Console.WriteLine($"  {memories[passedResult.Index].Summary}");
        }
        
        Console.WriteLine("Rejected:");
        foreach (var memoryRecord in memories.Where(x => !passedResults.Any(r => r.Index == x.Id)))
        {
            Console.WriteLine($"  {memoryRecord.Summary}");
        }
       

        return passedResults.Select(x => memories[x.Index]).ToArray();
    }
}