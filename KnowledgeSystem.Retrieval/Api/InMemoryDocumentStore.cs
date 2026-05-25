using System.Diagnostics.CodeAnalysis;
using KnowledgeSystem.Embedding;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.Lexical;
using KnowledgeSystem.Retrieval.Api.Capabilities;
using KnowledgeSystem.Retrieval.Data;
using KnowledgeSystem.Vector;
using KnowledgeSystem.Vector.Hnsw;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Retrieval.Api;

// TODO we need brute force for small number and then HNSW. Also would be good to have the insertion pipeline. Overall, this PoC needs to be rewritten

/// <summary>
///     An in-memory document store that supports adding and removing documents at runtime.
///     Implements vector search (via HNSW) and lexical search (via BM25).
/// </summary>
public sealed class InMemoryDocumentStore : IDocumentStore, IVectorSearchStore, ILexicalSearchStore, IDisposable
{
    private readonly ILogger<InMemoryDocumentStore> _logger;
    private readonly IIndexStateTracker _stateTracker;
    private readonly IEmbeddingService _embeddingService;
    private readonly MutableHnswIndex _hnsw;
    private readonly HnswVectorStoreAdapter _vectorStoreAdapter;
    private readonly Dictionary<int, EmdChunk> _chunkById = new();
    private readonly Dictionary<string, EmdDocument> _documentsByPath = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryDocumentStore(ILogger<InMemoryDocumentStore> logger, string storeId, IIndexStateTracker stateTracker, IEmbeddingService embeddingService)
    {
        _logger = logger;
        StoreId = storeId;
        _stateTracker = stateTracker;
        _embeddingService = embeddingService;
        _hnsw = new MutableHnswIndex(embeddingService.Dimension, maxConnectionsLane: 16, maxConnectionsDense: 32, efConstruction: 200);
        _vectorStoreAdapter = new HnswVectorStoreAdapter(_hnsw);
        LexicalIndex = new LexicalIndex();
    }

    public string StoreId { get; }

    public LexicalIndex LexicalIndex { get; }

    public IReadOnlySet<EmdDocument> ListDocuments()
    {
        return _documentsByPath.Values.ToHashSet();
    }

    public bool TryGetDocumentByPath(string path, [NotNullWhen(true)] out EmdDocument? document)
    {
        return _documentsByPath.TryGetValue(path, out document);
    }

    public bool TryGetChunk(int chunkId, [NotNullWhen(true)] out EmdChunk? chunk)
    {
        return _chunkById.TryGetValue(chunkId, out chunk);
    }

    public async Task AddDocument(EmdDocument document)
    {
        _documentsByPath[document.Path] = document;
        _stateTracker.AddDocument(document.Path);

        var allChunks = document.AttachedNodes.Values.SelectMany(node => node.Chunks).ToList();
        await EmbedAndAddChunksAsync(allChunks, document.Path);

        await _stateTracker.SaveChangesAsync();
        RebuildLexicalIndex();
    }

    public Task RemoveDocument(EmdDocument document)
    {
        _documentsByPath.Remove(document.Path);

        // Remove all chunks for this document from HNSW and state tracker:
        var chunkRecords = _stateTracker.GetAllChunkRecords()
            .Where(record => record.DocumentPath == document.Path)
            .ToList();

        foreach (var record in chunkRecords)
        {
            var vector = _hnsw.Vectors[record.ChunkId];
            if (vector != null)
            {
                _hnsw.Remove(vector);
            }

            _chunkById.Remove(record.ChunkId);
        }

        _stateTracker.RemoveDocument(document.Path);
        RebuildLexicalIndex();

        return Task.CompletedTask;
    }

    public IEmbeddingService EmbeddingService => _embeddingService;

    IReadOnlyVectorStore IVectorSearchStore.VectorStore => _vectorStoreAdapter;

    public async Task<VectorSearchResult[]> SearchAsync(string query, int k, CancellationToken cancellationToken = default)
    {
        var queryVector = await _embeddingService.EmbedAsync(query, cancellationToken);
        return _vectorStoreAdapter.Search(queryVector.Span, k);
    }

    public async Task<VectorSearchResult[][]> SearchAsync(string[] queries, int k, CancellationToken cancellationToken = default)
    {
        var queryVectors = await _embeddingService.EmbedBatchAsync(queries, cancellationToken);
        return queryVectors.Select(vector => _vectorStoreAdapter.Search(vector.Span, k)).ToArray();
    }

    public int GetChunkFrequency(string term) => LexicalIndex.GetChunkFrequency(term);

    public IReadOnlyDictionary<string, int> GetChunkTokenSet(int chunkId) => LexicalIndex.GetChunkTokenSet(chunkId);

    public Bm25Result[] SearchBm25(string query) => LexicalIndex.SearchBm25(query);

    // Slow. Ideally we have small ops
    private async Task EmbedAndAddChunksAsync(List<EmdChunk> chunks, string documentPath)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        var batchTexts = chunks.Select(chunk => chunk.ChunkText).ToList();
        var embeddings = await _embeddingService.EmbedBatchAsync(batchTexts);

        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            var embedding = embeddings[index];
            var storedVector = _hnsw.Insert(embedding.ToArray());

            _chunkById[storedVector.Index] = chunk;
            _stateTracker.AddChunk(chunk.Hash.ToHexString(), storedVector.Index, documentPath);
        }
    }

    private void RebuildLexicalIndex()
    {
        LexicalIndex.Build(_documentsByPath.Count, _chunkById);
    }

    public void Dispose()
    {
        // Empty
    }

    public ValueTask DisposeAsync()
    {
        // Empty
        
        return ValueTask.CompletedTask;
    }
}
