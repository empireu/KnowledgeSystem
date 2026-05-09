using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.Hnsw;
using KnowledgeSystem.Retrieval.Data;
using KnowledgeSystem.Retrieval.Embeddings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Retrieval;

public sealed class RagEngine
{
    private readonly RagDbContext _db;
    private readonly IEmbeddingService _embeddingService;
    private readonly RagOptions _options;
    private readonly Chunker _chunker;

    private MutableHnswIndex? _hnsw;
    private EmdRepository? _repo;

    public RagEngine(RagDbContext db, IEmbeddingService embeddingService, IOptions<RagOptions> options)
    {
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
        await _db.Database.EnsureCreatedAsync(cancellationToken);

        // We accept the synchronous call in here.
        _hnsw = File.Exists(_options.HnswIndexPath)
            ? MutableHnswIndex.LoadFromFile(_options.HnswIndexPath, _embeddingService.Dimension)
            : new MutableHnswIndex(
                _embeddingService.Dimension,
                maxConnectionsLane: _options.MaxConnectionsLane,
                maxConnectionsDense: _options.MaxConnectionsDense,
                efConstruction: _options.EfConstruction
            );

        if (_hnsw.Dimension != _embeddingService.Dimension)
        {
            throw new InvalidDataException("Stored HNSW doesn't match the configured embedding service");
        }
        
        await SynchronizeAsync(cancellationToken);
    }

    /// <summary>
    ///     Saves the HNSW index to disk. Call this after sync or modifications.
    /// </summary>
    public void SaveHnsw()
    {
        _hnsw?.Save(_options.HnswIndexPath);
    }

    /// <summary>
    ///     Compares the current repository state with the database and updates accordingly.
    /// </summary>
    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
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

        // Deleted files:
        var deletedPaths = dbPaths.Except(repoPaths).ToList();
        await RemoveDeletedFilesAsync(deletedPaths, cancellationToken);

        // New files:
        var newPaths = repoPaths.Except(dbPaths).ToList();
        await AddNewFilesAsync(_repo, newPaths, cancellationToken);

        // Existing files. Diff the chunks by hash:
        var existingPaths = repoPaths.Intersect(dbPaths).ToList();
        await SyncExistingFilesAsync(_repo, existingPaths, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        SaveHnsw();
    }

    /// <summary>
    ///     Searches for the <paramref name="k"/> chunks most similar to the query text.
    /// </summary>
    public async Task<VectorSearchResult[]> SearchAsync(string query, int k, int efSearch = 200, CancellationToken cancellationToken = default)
    {
        var queryVector = await _embeddingService.EmbedAsync(query, cancellationToken);
        return Hnsw.Search(queryVector.Span, k, efSearch);
    }
    
    /// <summary>
    ///     Searches for the <paramref name="k"/> chunks most similar to the query text batch.
    /// </summary>
    public async Task<VectorSearchResult[][]> SearchAsync(string[] queries, int k, int efSearch = 200, CancellationToken cancellationToken = default)
    {
        var queryVectors = await _embeddingService.EmbedBatchAsync(queries, cancellationToken);
       
        // Synchronous, compute-heavy in this async?
        // We may want to fix that at some point.
        return queryVectors.Select(x => Hnsw.Search(x.Span, k, efSearch)).ToArray();
    }
    
    private async Task RemoveDeletedFilesAsync(List<string> deletedPaths, CancellationToken cancellationToken)
    {
        foreach (var path in deletedPaths)
        {
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
            var key = EmdReferencePath.CreateFile(path);
            var document = repo.Documents[key];

            _db.Documents.Add(new DocumentRecord
            {
                Path = path
            });
            
            await EmbedAndAddChunksAsync(document, cancellationToken);
        }
    }

    private async Task SyncExistingFilesAsync(EmdRepository repo, List<string> existingPaths, CancellationToken cancellationToken)
    {
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
            await RemoveChunksAsync(removedHashes, cancellationToken);

            // New chunks: in repo but not in DB
            var addedHashes = repoHashes.Except(dbHashes).ToList();
            var newChunks = document.AttachedNodes.Values
                .SelectMany(n => n.Chunks)
                .Where(c => addedHashes.Contains(c.Hash.ToHexString()))
                .ToList();

            await EmbedAndAddChunksAsync(newChunks, path, cancellationToken);
        }
    }

    private async Task RemoveChunksAsync(List<string> removedHashes, CancellationToken cancellationToken)
    {
        foreach (var hashHex in removedHashes)
        {
            var chunk = await _db.Chunks.FindAsync([hashHex], cancellationToken);
            if (chunk == null) continue;

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

    private async Task EmbedAndAddChunksAsync(List<EmdChunk> chunks, string documentPath, CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        var texts = chunks.Select(c => c.ChunkText).ToList();
        var embeddings = await _embeddingService.EmbedBatchAsync(texts, cancellationToken);

        for (var i = 0; i < chunks.Count; i++)
        {
            var vector = embeddings[i].Span.ToArray();
            var storedVector = Hnsw.Insert(vector);
            
            _db.Chunks.Add(new ChunkRecord
            {
                HashHex = chunks[i].Hash.ToHexString(),
                HnswId = storedVector.Index,
                DocumentPath = documentPath,
            });
        }
    }
}
