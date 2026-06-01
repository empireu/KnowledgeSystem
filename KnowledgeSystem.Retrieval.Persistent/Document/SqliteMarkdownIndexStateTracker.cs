using KnowledgeSystem.Retrieval.Api;

namespace KnowledgeSystem.Retrieval.Persistent.Document;

/// <summary>
///     SQLite-backed index state tracker. Persists sync state across restarts.
/// </summary>
public sealed class SqliteMarkdownIndexStateTracker(DiskMarkdownDocumentStoreTrackerDbContext db) : IDocumentIndexStateTracker
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
        db.Documents.Add(new MarkdownDocumentRecord { Path = path });
    }

    public void AddChunk(string hashHex, int chunkId, string documentPath)
    {
        db.Chunks.Add(new MarkdownDocumentChunkRecord
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
        db.Documents.Remove(new MarkdownDocumentRecord { Path = path });
    }

    public void RemoveChunk(string hashHex)
    {
        var chunk = db.Chunks.Find(hashHex);
        if (chunk != null)
        {
            db.Chunks.Remove(chunk);
        }
    }

    public List<TrackedDocumentChunkRecord> ReadAllChunkRecords()
    {
        return db.Chunks
            .Select(c => new TrackedDocumentChunkRecord(c.HashHex, c.ChunkId, c.DocumentPath))
            .ToList();
    }

    public List<TrackedDocumentChunkRecord> ReadAllChunkRecords(string documentPath)
    {
        return db.Chunks
            .Where(c => c.DocumentPath == documentPath)
            .Select(c => new TrackedDocumentChunkRecord(c.HashHex, c.ChunkId, c.DocumentPath))
            .ToList();
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await db.SaveChangesAsync(cancellationToken);
    }
}
