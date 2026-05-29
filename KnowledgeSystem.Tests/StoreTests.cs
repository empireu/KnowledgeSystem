using KnowledgeSystem.Embedding;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.Retrieval.Api.Store;
using KnowledgeSystem.Retrieval.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeSystem.Tests;

public class StoreTests
{
    private readonly NullLoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    private readonly IServiceProvider _serviceProvider = Host.CreateDefaultBuilder()
        .ConfigureServices(service =>
        {
            service.AddLogging(_ =>
            {
                // Empty
            });
        })
        .Build()
        .Services;
    
    #region Helpers

    private static EmdDocument CreateTestDocument(string path, string content)
    {
        var repository = new EmdRepository("", new Dictionary<EmdReferencePath, EmdDocument>());
        var document = EmdDocument.Parse(path, content);
        document.GenerateChunksAndLookups(new Chunker());
        return document;
    }

    private static EmdDocument CreateSimpleDocument(string path, string text)
    {
        return CreateTestDocument(path, $"# {path}\n\n{text}");
    }

    /// <summary>
    ///     A deterministic mock embedding service that generates normalized vectors based on text hash.
    /// </summary>
    private sealed class MockEmbeddingService(int dimension = 8) : IEmbeddingService
    {
        public int Dimension { get; } = dimension;

        public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ReadOnlyMemory<float>>(MakeVector(text));
        }

        public Task<ReadOnlyMemory<float>[]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            var results = new ReadOnlyMemory<float>[texts.Count];
            for (var index = 0; index < texts.Count; index++)
            {
                results[index] = MakeVector(texts[index]);
            }

            return Task.FromResult(results);
        }

        private float[] MakeVector(string text)
        {
            var hash = (uint)text.GetHashCode();
            var vector = new float[Dimension];
            for (var index = 0; index < Dimension; index++)
            {
                vector[index] = MathF.Sin(hash * (index + 1) * 0.1f);
            }

            var normSqr = vector.Sum(v => v * v);
            var norm = MathF.Sqrt(normSqr);
            if (norm > 0)
            {
                for (var index = 0; index < Dimension; index++)
                {
                    vector[index] /= norm;
                }
            }

            return vector;
        }

        public void Dispose()
        {
            
        }
    }

    #endregion

    #region InMemoryDocumentStore Tests

    [Fact]
    public async Task InMemoryStore_AddDocument_ListDocumentsReturnsIt()
    {
        var embeddingService = new MockEmbeddingService();
        var stateTracker = new InMemoryIndexStateTracker();
        var logger = _loggerFactory.CreateLogger<InMemoryDocumentStore>();
        var store = new InMemoryDocumentStore(logger, "test", stateTracker, embeddingService);

        var document = CreateSimpleDocument("test.md", "Hello world content for testing");
        await store.AddDocument(document);

        var documents = store.ListDocuments();
        Assert.Single(documents);
        Assert.Contains(documents, x => x.Path == "test.md");
    }

    [Fact]
    public async Task InMemoryStore_AddDocument_TryGetDocumentByPathSucceeds()
    {
        var embeddingService = new MockEmbeddingService();
        var stateTracker = new InMemoryIndexStateTracker();
        var logger = _loggerFactory.CreateLogger<InMemoryDocumentStore>();
        var store = new InMemoryDocumentStore(logger, "test", stateTracker, embeddingService);

        var document = CreateSimpleDocument("path/to/file.md", "Some content here");
        await store.AddDocument(document);

        Assert.True(store.TryGetDocumentByPath("path/to/file.md", out var retrieved));
        Assert.Equal("path/to/file.md", retrieved.Path);
    }

    [Fact]
    public async Task InMemoryStore_AddDocument_TryGetChunkSucceeds()
    {
        var embeddingService = new MockEmbeddingService();
        var stateTracker = new InMemoryIndexStateTracker();
        var logger = _loggerFactory.CreateLogger<InMemoryDocumentStore>();
        var store = new InMemoryDocumentStore(logger, "test", stateTracker, embeddingService);

        var document = CreateSimpleDocument("doc.md", "Content that will be chunked and indexed properly");
        await store.AddDocument(document);

        var documents = store.ListDocuments();
        Assert.Single(documents);
        var storedDocument = documents.First();
        Assert.NotEmpty(storedDocument.AttachedNodes.Values.SelectMany(node => node.Chunks));
        
        Assert.True(store.TryGetChunk(0, out var chunk));
        Assert.NotNull(chunk);
    }

    [Fact]
    public async Task InMemoryStore_AddDocument_SearchBm25ReturnsResults()
    {
        var embeddingService = new MockEmbeddingService();
        var stateTracker = new InMemoryIndexStateTracker();
        var logger = _loggerFactory.CreateLogger<InMemoryDocumentStore>();
        var store = new InMemoryDocumentStore(logger, "test", stateTracker, embeddingService);

        var document = CreateSimpleDocument("doc.md", "Red Judas vs Blue Judas");
        await store.AddDocument(document);

        var results = store.GetCapability<ILexicalSearchCapability>(ILexicalSearchCapability.CapabilityType).SearchBm25("red");
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task InMemoryStore_RemoveDocument_NoLongerListed()
    {
        var embeddingService = new MockEmbeddingService();
        var stateTracker = new InMemoryIndexStateTracker();
        var logger = _loggerFactory.CreateLogger<InMemoryDocumentStore>();
        var store = new InMemoryDocumentStore(logger, "test", stateTracker, embeddingService);

        var document = CreateSimpleDocument("doc.md", "Content to be removed");
        await store.AddDocument(document);
        Assert.Single(store.ListDocuments());

        await store.RemoveDocument(document);
        Assert.Empty(store.ListDocuments());
    }

    [Fact]
    public Task InMemoryStore_StoreId_ReturnsConfiguredId()
    {
        try
        {
            var embeddingService = new MockEmbeddingService();
            var stateTracker = new InMemoryIndexStateTracker();
            var logger = _loggerFactory.CreateLogger<InMemoryDocumentStore>();
            var store = new InMemoryDocumentStore(logger, "my-store", stateTracker, embeddingService);

            Assert.Equal("my-store", store.StoreId);
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    [Fact]
    public Task InMemoryStore_CapabilityCheck_ImplementsInterfaces()
    {
        try
        {
            var embeddingService = new MockEmbeddingService();
            var stateTracker = new InMemoryIndexStateTracker();
            var logger = _loggerFactory.CreateLogger<InMemoryDocumentStore>();
            var store = new InMemoryDocumentStore(logger, "test", stateTracker, embeddingService);

            Assert.IsType<IDocumentStore>(store, exactMatch: false);
            Assert.True(store.HasCapability(IVectorSearchCapability.CapabilityType));
            Assert.True(store.HasCapability(ILexicalSearchCapability.CapabilityType));
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    [Fact]
    public async Task InMemoryStore_MultipleDocuments_AllIndexed()
    {
        var embeddingService = new MockEmbeddingService();
        var stateTracker = new InMemoryIndexStateTracker();
        var logger = _loggerFactory.CreateLogger<InMemoryDocumentStore>();
        var store = new InMemoryDocumentStore(logger, "test", stateTracker, embeddingService);

        await store.AddDocument(CreateSimpleDocument("a.md", "Alpha content about rockets"));
        await store.AddDocument(CreateSimpleDocument("b.md", "Beta content about satellites"));

        Assert.Equal(2, store.ListDocuments().Count);

        var bm25Results = store.GetCapability<ILexicalSearchCapability>(ILexicalSearchCapability.CapabilityType).SearchBm25("rockets");
        Assert.NotEmpty(bm25Results);
    }

    #endregion

    #region InMemoryIndexStateTracker Tests

    [Fact]
    public void InMemoryIndexStateTracker_AddDocument_GetKnownDocumentPathsReturnsIt()
    {
        var tracker = new InMemoryIndexStateTracker();
        tracker.AddDocument("test.md");

        var paths = tracker.GetKnownDocumentPaths();
        Assert.Contains("test.md", paths);
    }

    [Fact]
    public void InMemoryIndexStateTracker_AddChunk_TryGetChunkIdByHashSucceeds()
    {
        var tracker = new InMemoryIndexStateTracker();
        tracker.AddDocument("test.md");
        tracker.AddChunk("abc123", 42, "test.md");

        Assert.True(tracker.TryGetChunkIdByHash("abc123", out var chunkId));
        Assert.Equal(42, chunkId);
    }

    [Fact]
    public void InMemoryIndexStateTracker_RemoveDocument_RemovesAssociatedChunks()
    {
        var tracker = new InMemoryIndexStateTracker();
        tracker.AddDocument("test.md");
        tracker.AddChunk("hash1", 1, "test.md");
        tracker.AddChunk("hash2", 2, "test.md");

        tracker.RemoveDocument("test.md");

        Assert.Empty(tracker.GetKnownDocumentPaths());
        Assert.Empty(tracker.GetKnownChunkHashes("test.md"));
    }

    [Fact]
    public void InMemoryIndexStateTracker_RemoveChunk_NoLongerFindable()
    {
        var tracker = new InMemoryIndexStateTracker();
        tracker.AddDocument("test.md");
        tracker.AddChunk("hash1", 1, "test.md");

        tracker.RemoveChunk("hash1");

        Assert.False(tracker.TryGetChunkIdByHash("hash1", out _));
    }

    [Fact]
    public async Task InMemoryIndexStateTracker_PrepareForUseAsync_DoesNotThrow()
    {
        var tracker = new InMemoryIndexStateTracker();
        await tracker.PrepareForUseAsync();
    }

    [Fact]
    public async Task InMemoryIndexStateTracker_SaveChangesAsync_DoesNotThrow()
    {
        var tracker = new InMemoryIndexStateTracker();
        tracker.AddDocument("test.md");
        await tracker.SaveChangesAsync();
    }

    [Fact]
    public void InMemoryIndexStateTracker_GetAllChunkRecords_ReturnsAll()
    {
        var tracker = new InMemoryIndexStateTracker();
        tracker.AddDocument("doc1.md");
        tracker.AddDocument("doc2.md");
        tracker.AddChunk("hash1", 1, "doc1.md");
        tracker.AddChunk("hash2", 2, "doc2.md");

        var records = tracker.ReadAllChunkRecords();
        Assert.Equal(2, records.Count);
    }

    #endregion

    #region StoreManager Tests

    [Fact]
    public async Task StoreManager_CreateInMemoryStore_ReturnsWritableStore()
    {
        var embeddingService = new MockEmbeddingService();
        var manager = new StoreManager(_serviceProvider, embeddingService);

        var store = await manager.CreateInMemoryStoreAsync(new InMemoryStoreDescription { StoreId = "test" });

        Assert.NotNull(store);
        Assert.Equal("test", store.StoreId);
        Assert.IsAssignableFrom<IDocumentStore>(store);
    }

    [Fact]
    public async Task StoreManager_GetStore_ReturnsCreatedStore()
    {
        var embeddingService = new MockEmbeddingService();
        var manager = new StoreManager(_serviceProvider, embeddingService);

        await manager.CreateInMemoryStoreAsync(new InMemoryStoreDescription { StoreId = "my-store" });

        var store = manager.GetStore("my-store");
        Assert.NotNull(store);
        Assert.Equal("my-store", store.StoreId);
    }

    [Fact]
    public Task StoreManager_GetRequiredStore_ThrowsWhenMissing()
    {
        try
        {
            var embeddingService = new MockEmbeddingService();
            var manager = new StoreManager(_serviceProvider, embeddingService);

            Assert.Throws<KeyNotFoundException>(() => manager.GetRequiredStore("nonexistent"));
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    [Fact]
    public async Task StoreManager_CreateStore_DuplicateIdThrows()
    {
        var embeddingService = new MockEmbeddingService();
        var manager = new StoreManager(_serviceProvider, embeddingService);

        await manager.CreateInMemoryStoreAsync(new InMemoryStoreDescription { StoreId = "dup" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.CreateInMemoryStoreAsync(new InMemoryStoreDescription { StoreId = "dup" }));
    }

    [Fact]
    public async Task StoreManager_RemoveStore_NoLongerAccessible()
    {
        var embeddingService = new MockEmbeddingService();
        var manager = new StoreManager(_serviceProvider, embeddingService);

        await manager.CreateInMemoryStoreAsync(new InMemoryStoreDescription { StoreId = "temp" });
        Assert.NotNull(manager.GetStore("temp"));

        await manager.RemoveStoreAsync("temp");
        Assert.Null(manager.GetStore("temp"));
    }

    [Fact]
    public async Task StoreManager_GetActiveStoreIds_ReturnsAllIds()
    {
        var embeddingService = new MockEmbeddingService();
        var manager = new StoreManager(_serviceProvider, embeddingService);

        await manager.CreateInMemoryStoreAsync(new InMemoryStoreDescription { StoreId = "a" });
        await manager.CreateInMemoryStoreAsync(new InMemoryStoreDescription { StoreId = "b" });

        var ids = manager.GetActiveStoreIds();
        Assert.Equal(2, ids.Count);
        Assert.Contains("a", ids);
        Assert.Contains("b", ids);
    }

    #endregion

    #region Interface Capability Pattern Tests

    [Fact]
    public async Task InMemoryStore_CapabilityPattern_CanCastToVectorSearch()
    {
        var embeddingService = new MockEmbeddingService();
        var stateTracker = new InMemoryIndexStateTracker();
        var logger = _loggerFactory.CreateLogger<InMemoryDocumentStore>();
        IReadOnlyDocumentStore store = new InMemoryDocumentStore(logger, "test", stateTracker, embeddingService);

        // Add a document via the writable interface:
        var document = CreateSimpleDocument("doc.md", "Test content");
        await ((IDocumentStore)store).AddDocument(document);

        // Pattern: check capability via HasCapability/GetCapability
        if (store.HasCapability(IVectorSearchCapability.CapabilityType))
        {
            var vectorCapability = store.GetCapability<IVectorSearchCapability>(IVectorSearchCapability.CapabilityType);
            Assert.Same(embeddingService, vectorCapability.EmbeddingService);
            Assert.NotNull(vectorCapability.VectorStore);
        }
        else
        {
            Assert.Fail("InMemoryDocumentStore should support IVectorSearchStore capability");
        }
    }

    [Fact]
    public async Task InMemoryStore_CapabilityPattern_CanCastToLexicalSearch()
    {
        var embeddingService = new MockEmbeddingService();
        var stateTracker = new InMemoryIndexStateTracker();
        var logger = _loggerFactory.CreateLogger<InMemoryDocumentStore>();
        IReadOnlyDocumentStore store = new InMemoryDocumentStore(logger, "test", stateTracker, embeddingService);

        var document = CreateSimpleDocument("doc.md", "Test content for lexical search");
        await ((IDocumentStore)store).AddDocument(document);

        if (store.HasCapability(ILexicalSearchCapability.CapabilityType))
        {
            var lexicalCapability = store.GetCapability<ILexicalSearchCapability>(ILexicalSearchCapability.CapabilityType);
            var results = lexicalCapability.SearchBm25("lexical");
            Assert.NotEmpty(results);
        }
        else
        {
            Assert.Fail("InMemoryDocumentStore should support ILexicalSearchStore capability");
        }
    }

    [Fact]
    public async Task InMemoryStore_CapabilityPattern_CanCastToWritableStore()
    {
        var embeddingService = new MockEmbeddingService();
        var stateTracker = new InMemoryIndexStateTracker();
        var logger = _loggerFactory.CreateLogger<InMemoryDocumentStore>();
        await using IReadOnlyDocumentStore store = new InMemoryDocumentStore(logger, "test", stateTracker, embeddingService);

        if (store is IDocumentStore writableStore)
        {
            var document = CreateSimpleDocument("new.md", "New document content");
            await writableStore.AddDocument(document);
            Assert.True(store.TryGetDocumentByPath("new.md", out _));
        }
        else
        {
            Assert.Fail("InMemoryDocumentStore should implement IDocumentStore");
        }
    }

    #endregion
}
