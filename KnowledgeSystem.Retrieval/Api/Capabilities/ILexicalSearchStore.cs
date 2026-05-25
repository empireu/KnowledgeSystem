using KnowledgeSystem.Lexical;

namespace KnowledgeSystem.Retrieval.Api.Capabilities;

public interface ILexicalSearchStore : IReadOnlyDocumentStore
{
    /// <summary>
    ///     Returns the number of chunks containing the given term, or 0 if the term is not in the index.
    /// </summary>
    public int GetChunkFrequency(string term);

    /// <summary>
    ///     Returns the tokens for the given chunk.
    /// </summary>
    public IReadOnlyDictionary<string, int> GetChunkTokenSet(int chunkId);

    /// <summary>
    ///     Searches for chunks matching the query using BM25.
    /// </summary>
    public Bm25Result[] SearchBm25(string query);
}