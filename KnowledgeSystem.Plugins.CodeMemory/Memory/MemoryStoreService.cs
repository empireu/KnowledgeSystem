using KnowledgeSystem.Ai;
using KnowledgeSystem.Embedding;
using KnowledgeSystem.Vector;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Plugins.CodeMemory.Memory;

public sealed class MemoryStoreService(
    ILogger<MemoryStoreService> logger,
    IOptions<MemorySystemConfig> options,
    IEmbeddingService embeddingService,
    ILogprobGatingService gateService
) : IHostedService
{
    private sealed class Store
    {
        public required MemoryDbContext Db { get; init; }

        public required Dictionary<string, MemoryLexicalIndex> LexicalIndices { get; init; }
        
        public readonly SemaphoreSlim DbSemaphore = new(1, 1);
    }

    private Store? _store;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var dbContextOptions = new DbContextOptionsBuilder<MemoryDbContext>()
            .UseSqlite($"Data Source=memory.db")
            .Options;

        var db = new MemoryDbContext(dbContextOptions);
        
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var groupsByNamespace = await db.Memories
            .Select(x => new { x.Id, x.Namespace, x.Summary })
            .GroupBy(x => x.Namespace)
            .ToListAsync(cancellationToken: cancellationToken);
        
        var lexicalIndices = groupsByNamespace.ToDictionary(grouping => grouping.Key, grouping =>
        {
            var lexical = new MemoryLexicalIndex();
            lexical.Build(grouping.Select(x => (x.Id, x.Summary)));
            return lexical;
        });
        
        _store = new Store
        {
            Db = db,
            LexicalIndices = lexicalIndices
        };
        
        logger.LogInformation("Loaded {count} memories", await db.Memories.CountAsync(cancellationToken: cancellationToken));
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
    
    public async Task<int> CreateMemory(string @namespace, string summary, string content, DateTime utcCreatedAt, CancellationToken cancellationToken)
    {
        if (_store == null)
        {
            throw new InvalidOperationException("Memory store not initialized");
        }
        
        await _store.DbSemaphore.WaitAsync(cancellationToken);

        try
        {
            var embedding = await embeddingService.EmbedAsync(summary, cancellationToken);

            var record = new MemoryRecord
            {
                Namespace = @namespace,
                Summary = summary,
                Content = content,
                UtcCreatedAt = utcCreatedAt
            };
            
            record.LoadEmbedding(embedding.Span);
            
            _store.Db.Memories.Add(record);
            await _store.Db.SaveChangesAsync(CancellationToken.None);

            if (!_store.LexicalIndices.TryGetValue(@namespace, out var lexical))
            {
                lexical = new MemoryLexicalIndex();
                _store.LexicalIndices.Add(@namespace, lexical);
            }
            
            lexical.Add(record.Id, summary);
            
            logger.LogInformation(
                "Creating memory {id}: {summary}: {content}",
                record.Id, 
                summary,
                content
            );
        
            return record.Id;
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

            if (_store.LexicalIndices.TryGetValue(record.Namespace, out var lexical))
            {
                lexical.Remove(id, record.Summary);
            }

            _store.Db.Memories.Remove(record);
            await _store.Db.SaveChangesAsync(CancellationToken.None);
            
            logger.LogInformation("Deleted memory {index}: {summary}", id, record.Summary);
            
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

    private const int RrfK = 60;
    
    public async Task<MemoryRecord[]> SearchAsync(string @namespace, string query, CancellationToken cancellationToken)
    {
        if (_store == null)
        {
            throw new InvalidOperationException("Memory store not initialized");
        }

        if (!_store.LexicalIndices.ContainsKey(@namespace))
        {
            return [];
        }
        
        var config = options.Value;

        var embedding = await embeddingService.EmbedAsync(query, cancellationToken);
        
        MemoryRecord[] memories;
        await _store.DbSemaphore.WaitAsync(cancellationToken);
        try
        {
            var memoryRecordsInNamespace = await _store.Db.Memories
                .Where(x => x.Namespace.Equals(@namespace))
                .ToListAsync(cancellationToken: cancellationToken);

            var buffer = new float[embeddingService.Dimension];
            var vectorResults = memoryRecordsInNamespace.Select(record =>
                {
                    record.GetEmbedding(buffer);
                    var score = VectorObjective.AdjustedCosineSimilarity(buffer, embedding.Span);
                    return (score, record);
                })
                .OrderBy(x => x.score)
                .Take(config.ResultsPerMethod)
                .ToList();
            
            var bm25Results = _store.LexicalIndices[@namespace].SearchBm25(
                query, 
                config.ResultsPerMethod
            );

            if (vectorResults.Count == 0 && bm25Results.Length == 0)
            {
                return [];
            }

            // Build rank maps for RRF:
            var vectorRanks = new Dictionary<int, int>(vectorResults.Count);
            for (var i = 0; i < vectorResults.Count; i++)
            {
                vectorRanks[vectorResults[i].record.Id] = i + 1;
            }

            var bm25Ranks = new Dictionary<int, int>(bm25Results.Length);
            for (var i = 0; i < bm25Results.Length; i++)
            {
                bm25Ranks[bm25Results[i].ChunkId] = i + 1;
            }

            // RRF across both result sets like the fast context:
            var rrfScores = new Dictionary<int, double>();
            foreach (var (id, rank) in vectorRanks)
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
                .Take(config.FusedResults)
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
        
        var gateResults = await gateService.AreRelevantAsync(
            query,
            memories.Select(x => x.Summary).ToList(),
            config.GateThreshold,
            cancellationToken
        );

        return memories
            .Where((_, index) => gateResults[index])
            .Take(config.TopN)
            .ToArray();
    }
}