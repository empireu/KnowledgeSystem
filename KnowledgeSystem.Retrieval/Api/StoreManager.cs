using System.Collections.Concurrent;
using KnowledgeSystem.Embedding;
using KnowledgeSystem.Retrieval.Data;
using KnowledgeSystem.Retrieval.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Retrieval.Api;

/// <summary>
///     Manages the lifecycle of knowledge stores.
/// </summary>
public sealed class StoreManager : IStoreManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, IReadOnlyDocumentStore> _stores = new(StringComparer.OrdinalIgnoreCase);

    public StoreManager(IServiceProvider serviceProvider, IEmbeddingService embeddingService, ILoggerFactory loggerFactory)
    {
        _serviceProvider = serviceProvider;
        _embeddingService = embeddingService;
        _loggerFactory = loggerFactory;
    }

    public IReadOnlyDocumentStore? GetStore(string storeId)
    {
        return _stores.TryGetValue(storeId, out var store) ? store : null;
    }

    public IReadOnlyDocumentStore GetRequiredStore(string storeId)
    {
        return GetStore(storeId) ?? throw new KeyNotFoundException($"Store with ID \"{storeId}\" not found");
    }

    public async Task<IReadOnlyDocumentStore> CreateDiskStoreAsync(DiskStoreConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (_stores.ContainsKey(configuration.StoreId))
        {
            throw new InvalidOperationException($"Store with ID \"{configuration.StoreId}\" already exists");
        }

        // This crap needs to be refactord
        var options = new RagOptions
        {
            StoreId = configuration.StoreId,
            RepositoryPath = configuration.RepositoryPath,
            DatabasePath = configuration.DatabasePath,
            HnswIndexPath = configuration.HnswIndexPath,
            EmbeddingEndpoint = "",
            EmbeddingModel = "",
            EmbeddingApiKey = "",
            EmbeddingDimension = _embeddingService.Dimension
        };

        var dbContextOptions = new DbContextOptionsBuilder<RagDbContext>()
            .UseSqlite($"Data Source={configuration.DatabasePath}")
            .Options;

        var dbContext = new RagDbContext(dbContextOptions);
        var stateTracker = new SqliteIndexStateTracker(dbContext);
        var logger = _loggerFactory.CreateLogger<RagEngine>();

        var engine = new RagEngine(logger, stateTracker, _embeddingService, Options.Create(options));
        await engine.InitializeAsync(cancellationToken);

        _stores[configuration.StoreId] = engine;
        return engine;
    }

    public Task<IReadOnlyDocumentStore> CreateInMemoryStoreAsync(InMemoryStoreConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (_stores.ContainsKey(configuration.StoreId))
        {
            throw new InvalidOperationException($"Store with ID '{configuration.StoreId}' already exists");
        }

        // In-memory stores use the InMemoryIndexStateTracker and don't load from disk
        // They require an IDocumentStore implementation for adding documents at runtime
        // For now, create a lightweight in-memory store backed by the InMemoryIndexStateTracker
        var stateTracker = new InMemoryIndexStateTracker();
        var logger = _loggerFactory.CreateLogger<InMemoryDocumentStore>();

        var store = new InMemoryDocumentStore(logger, configuration.StoreId, stateTracker, _embeddingService);
        _stores[configuration.StoreId] = store;

        return Task.FromResult<IReadOnlyDocumentStore>(store);
    }

    public async Task RemoveStoreAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (_stores.TryRemove(storeId, out var store))
        {
            await store.DisposeAsync();
        }
    }

    public IReadOnlyList<string> GetActiveStoreIds()
    {
        return _stores.Keys.ToList();
    }
}
