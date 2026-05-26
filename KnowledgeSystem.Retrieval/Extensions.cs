using KnowledgeSystem.Retrieval.Api.Store;
using KnowledgeSystem.Vector;

namespace KnowledgeSystem.Retrieval;

public static class Extensions
{
    /// <summary>
    ///     Gets the vector search capability from a vector-search-capable store.
    /// </summary>
    public static IVectorStore AsVector(this IReadOnlyVectorStore store)
    {
        if (store is not IVectorStore result)
        {
            throw new InvalidOperationException($"Vector store of type {store} doesn't support vector search!");
        }
        
        return result;
    }
    
    /// <summary>
    ///     Gets the lexical search capability from a store that supports it.
    /// </summary>
    public static ILexicalSearchStore AsLexical(this IReadOnlyDocumentStore store)
    {
        return store.GetCapability<ILexicalSearchStore>(ILexicalSearchStore.CapabilityType);
    }
}