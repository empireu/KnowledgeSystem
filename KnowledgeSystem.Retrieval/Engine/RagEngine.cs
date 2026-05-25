using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.Vector.Hnsw;
using KnowledgeSystem.Retrieval.Data;
using KnowledgeSystem.Retrieval.Embeddings;
using KnowledgeSystem.Retrieval.Lexical;
using KnowledgeSystem.Vector;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ReSharper disable LoopCanBeConvertedToQuery
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Retrieval.Engine;

/// <summary>
///     The RAG engine handles embedding queries and retrieving extracts from the repo using the HNSW.
/// </summary>
public sealed class RagEngine
{
    private readonly ILogger<RagEngine> _logger;
    private readonly RagDbContext _db;
    private readonly IEmbeddingService _embeddingService;
    private readonly RagOptions _options;
    private readonly Chunker _chunker;

    private MutableHnswIndex? _hnsw;
    private EmdRepository? _repo;

    /// <summary>
    ///     Maps HNSW vector index to the corresponding chunk.
    ///     Populated during <see cref="SynchronizeAsync"/>.
    /// </summary>
    private readonly Dictionary<int, EmdChunk> _chunkByHnswId = new();

    public RagEngine(ILogger<RagEngine> logger, RagDbContext db, IEmbeddingService embeddingService, IOptions<RagOptions> options)
    {
        _logger = logger;
        _db = db;
        _embeddingService = embeddingService;
        _options = options.Value;
        _chunker = new Chunker(_options.MaxChunkLength);
    }

    /// <summary>
    ///     The loaded HNSW index. Available after calling <see cref="InitializeAsync"/>.
    /// </summary>
    public MutableHnswIndex Hnsw => _hnsw ?? throw new InvalidOperationException("RAG engine not initialized");

    /// <summary>
    ///     The loaded repo. Available after calling <see cref="InitializeAsync"/>.
    /// </summary>
    public EmdRepository Repo => _repo ?? throw new InvalidDataException("RAG engine not initialized");

    /// <summary>
    ///     The lexical index for keyword search. Available after calling <see cref="SynchronizeAsync"/>.
    /// </summary>
    public LexicalIndex LexicalIndex { get; } = new();

    #region Setup
    
    /// <summary>
    ///     Initializes the RAG engine:
    ///     <list type="bullet">
    ///         <item><description>Ensures the database exists</description></item>
    ///         <item><description>Loads or creates the HNSW</description></item>
    ///         <item><description>Loads the fresh repository from disk</description></item>
    ///         <item><description>Synchronizes the repository with the RAG</description></item>
    ///     </list>
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing RAG Engine");
        
        await _db.Database.EnsureCreatedAsync(cancellationToken);
        
        // We accept the synchronous call in here.
        if (File.Exists(_options.HnswIndexPath))
        {
            _logger.LogInformation("Loading HNSW from {path}", _options.HnswIndexPath);
            _hnsw = MutableHnswIndex.LoadFromFile(_options.HnswIndexPath, _embeddingService.Dimension);
        }
        else
        {
            _logger.LogInformation("Creating fresh HNSW");
            _hnsw = new MutableHnswIndex(
                _embeddingService.Dimension,
                maxConnectionsLane: _options.MaxConnectionsLane,
                maxConnectionsDense: _options.MaxConnectionsDense,
                efConstruction: _options.EfConstruction
            );
        }

        if (_hnsw.Dimension != _embeddingService.Dimension)
        {
            throw new InvalidDataException("Stored HNSW doesn't match the configured embedding service");
        }
        
        await SynchronizeAsync(cancellationToken);
    }

    /// <summary>
    ///     Compares the current repository state with the database and updates accordingly.
    /// </summary>
    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        var changed = false;
        
        _logger.LogInformation("Loading HNSW repository");
        
        _repo = await EmdRepository.LoadAsync(
            _options.RepositoryPath,
            _chunker,
            _options.MaxReadTasks,
            cancellationToken
        );

        var repoPaths = _repo.Documents.Keys
            .Select(k => k.RepositoryRelativePath)
            .ToHashSet();
        
        var dbPaths = await _db.Documents
            .Select(d => d.Path)
            .ToHashSetAsync(cancellationToken);

        _logger.LogInformation("Repo paths: {repo}, DB paths: {db}", repoPaths.Count, dbPaths.Count);
        
        // Deleted files:
        var deletedPaths = dbPaths.Except(repoPaths).ToList();
        if (deletedPaths.Count > 0)
        {
            changed = true;
            await RemoveDeletedFilesAsync(deletedPaths, cancellationToken);
        }

        // New files:
        var newPaths = repoPaths.Except(dbPaths).ToList();
        if (newPaths.Count > 0)
        {
            changed = true;
            await AddNewFilesAsync(_repo, newPaths, cancellationToken);
        }

        // Existing files. Diff the chunks by hash:
        var existingPaths = repoPaths.Intersect(dbPaths).ToList();
        if (await SyncExistingFilesAsync(_repo, existingPaths, cancellationToken))
        {
            changed = true;
        }

        if (changed)
        {
            _logger.LogInformation("Freezing DB. Vectors: {vec}", _hnsw?.Vectors.Sum(x => x == null ? 0 : 1));
            await _db.SaveChangesAsync(cancellationToken);
            _hnsw?.Save(_options.HnswIndexPath);
        }
        
        _chunkByHnswId.Clear();
        
        var allChunkRecords = await _db.Chunks.ToListAsync(cancellationToken);
        foreach (var record in allChunkRecords)
        {
            var fileKey = EmdReferencePath.CreateFile(record.DocumentPath);
        
            if (_repo.Documents.TryGetValue(fileKey, out var document) && document.ChunksByHexHash.TryGetValue(record.HashHex, out var chunk))
            {
                _chunkByHnswId[record.HnswId] = chunk;
            }
        }

        _logger.LogInformation("Building lexical index from {count} chunks", _chunkByHnswId.Count);
      
        LexicalIndex.Build(_repo.Documents.Count, _chunkByHnswId);
    }
    
    private async Task RemoveDeletedFilesAsync(List<string> deletedPaths, CancellationToken cancellationToken)
    {
        foreach (var path in deletedPaths)
        {
            _logger.LogInformation("Deleting {path}", path);
            
            var chunks = await _db.Chunks
                .Where(c => c.DocumentPath == path)
                .ToListAsync(cancellationToken);

            foreach (var chunk in chunks)
            {
                var vector = Hnsw.Vectors[chunk.HnswId];
                if (vector != null)
                {
                    Hnsw.Remove(vector);
                }
            }

            _db.Chunks.RemoveRange(chunks);
            _db.Documents.Remove(new DocumentRecord
            {
                Path = path
            });
        }
    }

    private async Task AddNewFilesAsync(EmdRepository repo, List<string> newPaths, CancellationToken cancellationToken)
    {
        foreach (var path in newPaths)
        {
            _logger.LogInformation("Adding {path}", path);
            
            var key = EmdReferencePath.CreateFile(path);
            var document = repo.Documents[key];

            _db.Documents.Add(new DocumentRecord
            {
                Path = path
            });
            
            await EmbedAndAddChunksAsync(document, cancellationToken);
        }
    }

    private async Task<bool> SyncExistingFilesAsync(EmdRepository repo, List<string> existingPaths, CancellationToken cancellationToken)
    {
        var changed = false;
        
        foreach (var path in existingPaths)
        {
            var key = EmdReferencePath.CreateFile(path);
            var document = repo.Documents[key];

            var repoHashes = document.AttachedNodes.Values
                .SelectMany(n => n.Chunks)
                .Select(c => c.Hash.ToHexString())
                .ToHashSet();

            var dbHashes = await _db.Chunks
                .Where(c => c.DocumentPath == path)
                .Select(c => c.HashHex)
                .ToHashSetAsync(cancellationToken);

            // Removed chunks: in DB but not in repo
            var removedHashes = dbHashes.Except(repoHashes).ToList();

            if (removedHashes.Count > 0)
            {
                _logger.LogInformation("Updating {path}: deleted {num} chunks", path, removedHashes.Count);
                changed = true;
                await RemoveChunksAsync(removedHashes, cancellationToken);
            }

            // New chunks: in repo but not in DB
            var addedHashes = repoHashes.Except(dbHashes).ToList();
            var newChunks = document.AttachedNodes.Values
                .SelectMany(n => n.Chunks)
                .Where(c => addedHashes.Contains(c.Hash.ToHexString()))
                .ToList();

            if (newChunks.Count > 0)
            {
                _logger.LogInformation("Updating {path}: added {num} chunks", path, newChunks.Count);
                changed = true;
                await EmbedAndAddChunksAsync(newChunks, path, cancellationToken);
            }
        }

        return changed;
    }

    private async Task RemoveChunksAsync(List<string> removedHashes, CancellationToken cancellationToken)
    {
        foreach (var hashHex in removedHashes)
        {
            var chunk = await _db.Chunks.FindAsync([hashHex], cancellationToken);
            
            if (chunk == null)
            {
                continue;
            }

            var vector = Hnsw.Vectors[chunk.HnswId];
            
            if (vector != null)
            {
                Hnsw.Remove(vector);
            }

            _db.Chunks.Remove(chunk);
        }
    }

    private async Task EmbedAndAddChunksAsync(EmdDocument document, CancellationToken cancellationToken)
    {
        var allChunks = document.AttachedNodes.Values
            .SelectMany(n => n.Chunks)
            .ToList();
        
        await EmbedAndAddChunksAsync(allChunks, document.Path, cancellationToken);
    }

    #region Embedding Pipeline
    
    private readonly struct EmbeddingTransfer
    {
        public required EmdChunk Chunk { get; init; }
        
        public required ReadOnlyMemory<float> Embedding { get; init; }
    }
    
    private async Task EmbedAndAddChunksAsync(List<EmdChunk> chunks, string documentPath, CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        var channel = Channel.CreateBounded<EmbeddingTransfer>(new BoundedChannelOptions(_options.ParallelInsert * (_options.EmbeddingBatchSize + 1))
        {
            SingleWriter = true,
            SingleReader = _options.ParallelInsert == 1,
            FullMode = BoundedChannelFullMode.Wait
        });
        
        var dbChunkRecords = new ConcurrentBag<ChunkRecord>();

        var producerTask = ProduceEmbeddingsTransfers(chunks, channel.Writer, cancellationToken);
       
        var consumerTasks = Enumerable
            .Range(0, _options.ParallelInsert)
            .Select(_ => ConsumeEmbeddingTransfers(channel.Reader, dbChunkRecords, documentPath, cancellationToken))
            .ToArray();

        await producerTask;
        await Task.WhenAll(consumerTasks);

        foreach (var record in dbChunkRecords)
        {
            _db.Chunks.Add(record);
        }
    }

    private async Task ProduceEmbeddingsTransfers(List<EmdChunk> chunks, ChannelWriter<EmbeddingTransfer> writer, CancellationToken cancellationToken)
    {
        try
        {
            var batchSize = _options.EmbeddingBatchSize;
        
            var queue = new Queue<EmdChunk>(chunks);
            var batch = new List<EmdChunk>(batchSize);
            var batchTexts = new List<string>(batchSize);
        
            while (queue.Count > 0)
            {
                while (queue.Count > 0 && batch.Count < batchSize)
                {
                    var front = queue.Dequeue();
                    batch.Add(front);
                    batchTexts.Add(front.ChunkText);
                }
            
                var embeddings = await _embeddingService.EmbedBatchAsync(batchTexts, cancellationToken);

                for (var index = 0; index < embeddings.Length; index++)
                {
                    var embedding = embeddings[index];
                    var chunk = batch[index];
                
                    await writer.WriteAsync(new EmbeddingTransfer
                    {
                        Chunk = chunk,
                        Embedding = embedding
                    }, cancellationToken);
                }
            
                batch.Clear();
                batchTexts.Clear();
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task ConsumeEmbeddingTransfers(ChannelReader<EmbeddingTransfer> reader, ConcurrentBag<ChunkRecord> databaseRecords, string documentPath, CancellationToken cancellationToken)
    {
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            while (reader.TryRead(out var transfer))
            {
                var vector = transfer.Embedding.ToArray();
                var storedVector = Hnsw.Insert(vector);

                databaseRecords.Add(new ChunkRecord
                {
                    HashHex = transfer.Chunk.Hash.ToHexString(),
                    HnswId = storedVector.Index,
                    DocumentPath = documentPath
                });
            }   
        }
    }
    
    #endregion
    
    #endregion

    public void Warmup()
    {
        if (_hnsw == null || _hnsw.Vectors.Count == 0)
        {
            return;
        }
 
        var query = new float[_hnsw.Dimension];
        var random = new Random();
        for (var q = 0; q < 100; q++)
        {
            for (var i = 0; i < query.Length; i++)
            {
                query[i] = (float)random.NextDouble();
            }
            
            IEmbeddingService.SanitizeNetworkResult(query);
            
            _hnsw.Search(query, 100, 1000);
        }
    }
    
    #region API
    
    public bool TryGetChunkByHnswId(int hnswId, [NotNullWhen(true)] out EmdChunk? chunk)
    {
        return _chunkByHnswId.TryGetValue(hnswId, out chunk);
    }
    
    public EmdChunk GetChunkByHnswId(int hnswId)
    {
        return _chunkByHnswId.TryGetValue(hnswId, out var chunk)
            ? chunk 
            : throw new KeyNotFoundException($"No chunk found for HNSW id {hnswId}");
    }

    /// <summary>
    ///     Searches for the <paramref name="k"/> chunks most similar to the query text.
    /// </summary>
    public async Task<VectorSearchResult[]> SearchAsync(string query, int k, int efSearch = 200, Predicate<int>? predicate = null, CancellationToken cancellationToken = default)
    {
        var queryVector = await _embeddingService.EmbedAsync(query, cancellationToken);
        return Hnsw.Search(queryVector.Span, k, efSearch, predicate);
    }
    
    /// <summary>
    ///     Searches for the <paramref name="k"/> chunks most similar to the query text batch.
    /// </summary>
    public async Task<VectorSearchResult[][]> SearchAsync(string[] queries, int k, int efSearch = 200, Predicate<int>? predicate = null, CancellationToken cancellationToken = default)
    {
        var queryVectors = await _embeddingService.EmbedBatchAsync(queries, cancellationToken);
       
        // Synchronous, compute-heavy in this async?
        // We may want to fix that at some point.
        return queryVectors.Select(x => Hnsw.Search(x.Span, k, efSearch, predicate)).ToArray();
    }
    
    #endregion
}
