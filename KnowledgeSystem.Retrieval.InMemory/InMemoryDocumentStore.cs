using System.Diagnostics.CodeAnalysis;
using KnowledgeSystem.Embedding;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.Lexical;
using KnowledgeSystem.Retrieval.Api;
using KnowledgeSystem.Retrieval.Api.Store;
using KnowledgeSystem.Vector;
using KnowledgeSystem.Vector.Hnsw;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Retrieval.InMemory;

// TODO we need brute force for small number and then HNSW. Also would be good to have the insertion pipeline. Overall, this PoC needs to be rewritten

/// <summary>
///     An in-memory document store that supports adding and removing documents at runtime.
///     Implements vector search (via HNSW) and lexical search (via BM25).
/// </summary>
public sealed class InMemoryDocumentStore : StoreBase, IDocumentStore, IDisposable
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

        RegisterCapability(IVectorSearchStore.CapabilityType, new VectorSearchCapability(this));
        RegisterCapability(ILexicalSearchStore.CapabilityType, new LexicalSearchCapability(this));
    }

    public override string StoreId { get; }

    public LexicalIndex LexicalIndex { get; }

    public override IReadOnlySet<EmdDocument> ListDocuments()
    {
        return _documentsByPath.Values.ToHashSet();
    }

    public override bool TryGetDocumentByPath(string path, [NotNullWhen(true)] out EmdDocument? document)
    {
        return _documentsByPath.TryGetValue(path, out document);
    }

    public override bool TryGetChunk(int chunkId, [NotNullWhen(true)] out EmdChunk? chunk)
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
        var chunkRecords = _stateTracker.ReadAllChunkRecords()
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

    public override ValueTask DisposeAsync()
    {
        // Empty
        
        return ValueTask.CompletedTask;
    }

    private sealed class VectorSearchCapability(InMemoryDocumentStore store) : IVectorSearchStore
    {
        public IEmbeddingService EmbeddingService => store._embeddingService;

        IReadOnlyVectorStore IVectorSearchStore.VectorStore => store._vectorStoreAdapter;

        public async Task<VectorSearchResult[]> SearchAsync(string query, int k, CancellationToken cancellationToken = default)
        {
            var queryVector = await store._embeddingService.EmbedAsync(query, cancellationToken);
            return store._vectorStoreAdapter.Search(queryVector.Span, k);
        }

        public async Task<VectorSearchResult[][]> SearchAsync(string[] queries, int k, CancellationToken cancellationToken = default)
        {
            var queryVectors = await store._embeddingService.EmbedBatchAsync(queries, cancellationToken);
            return queryVectors.Select(vector => store._vectorStoreAdapter.Search(vector.Span, k)).ToArray();
        }
    }

    private sealed class LexicalSearchCapability(InMemoryDocumentStore store) : ILexicalSearchStore
    {
        public int GetChunkFrequency(string term) => store.LexicalIndex.GetChunkFrequency(term);

        public IReadOnlyDictionary<string, int> GetChunkTokenSet(int chunkId) => store.LexicalIndex.GetChunkTokenSet(chunkId);

        public Bm25Result[] SearchBm25(string query) => store.LexicalIndex.SearchBm25(query);
    }
}
