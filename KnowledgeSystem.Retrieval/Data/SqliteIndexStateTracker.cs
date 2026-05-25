namespace KnowledgeSystem.Retrieval.Data;

/// <summary>
///     SQLite-backed index state tracker. Persists sync state across restarts.
/// </summary>
public sealed class SqliteIndexStateTracker(RagDbContext db) : IIndexStateTracker
{
    public async Task PrepareForUseAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    public HashSet<string> GetKnownDocumentPaths()
    {
        return db.Documents
            .Select(d => d.Path)
            .ToHashSet();
    }

    public HashSet<string> GetKnownChunkHashes(string documentPath)
    {
        return db.Chunks
            .Where(c => c.DocumentPath == documentPath)
            .Select(c => c.HashHex)
            .ToHashSet();
    }

    public bool TryGetChunkIdByHash(string hashHex, out int chunkId)
    {
        var record = db.Chunks.Find(hashHex);
        if (record != null)
        {
            chunkId = record.ChunkId;
            return true;
        }

        chunkId = 0;
        return false;
    }

    public void AddDocument(string path)
    {
        db.Documents.Add(new DocumentRecord { Path = path });
    }

    public void AddChunk(string hashHex, int chunkId, string documentPath)
    {
        db.Chunks.Add(new ChunkRecord
        {
            HashHex = hashHex,
            ChunkId = chunkId,
            DocumentPath = documentPath
        });
    }

    public void RemoveDocument(string path)
    {
        var chunks = db.Chunks.Where(c => c.DocumentPath == path).ToList();
        db.Chunks.RemoveRange(chunks);
        db.Documents.Remove(new DocumentRecord { Path = path });
    }

    public void RemoveChunk(string hashHex)
    {
        var chunk = db.Chunks.Find(hashHex);
        if (chunk != null)
        {
            db.Chunks.Remove(chunk);
        }
    }

    public IReadOnlyList<(string HashHex, int ChunkId, string DocumentPath)> GetAllChunkRecords()
    {
        return db.Chunks
            .Select(c => new ValueTuple<string, int, string>(c.HashHex, c.ChunkId, c.DocumentPath))
            .ToList();
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await db.SaveChangesAsync(cancellationToken);
    }
}
