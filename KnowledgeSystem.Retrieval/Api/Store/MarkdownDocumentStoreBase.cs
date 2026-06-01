using System.Diagnostics.CodeAnalysis;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;

namespace KnowledgeSystem.Retrieval.Api.Store;

/// <summary>
///     Useful base for stores.
/// </summary>
public abstract class MarkdownDocumentStoreBase : IReadOnlyMarkdownDocumentStore
{
    private readonly Dictionary<StoreCapabilityType, ISearchCapability> _capabilities = [];

    protected void RegisterCapability(StoreCapabilityType capability, ISearchCapability searchCapability)
    {
        if (!_capabilities.TryAdd(capability, searchCapability))
        {
            throw new ArgumentException($"Capability {capability} already exists");
        }
    }

    public bool HasCapability(StoreCapabilityType capabilityType)
    {
        return _capabilities.ContainsKey(capabilityType);
    }
    
    public T GetCapability<T>(StoreCapabilityType capability) where T : ISearchCapability
    {
        return (T)_capabilities[capability];
    }

    #region Abstract
    
    public abstract ValueTask DisposeAsync();
    public abstract string StoreId { get; }
    public abstract IReadOnlySet<EmdDocument> ListDocuments();
    public abstract bool TryGetDocumentByPath(string path, [NotNullWhen(true)] out EmdDocument? document);
    public abstract bool TryGetChunk(int chunkId, [NotNullWhen(true)] out EmdChunk? chunk);
    
    #endregion
}