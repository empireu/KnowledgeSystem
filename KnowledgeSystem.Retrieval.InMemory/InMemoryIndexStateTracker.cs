using KnowledgeSystem.Retrieval.Api;

namespace KnowledgeSystem.Retrieval.InMemory;

public sealed class InMemoryIndexStateTracker : IIndexStateTracker
{
    private readonly Dictionary<string, HashSet<string>> _documentChunkHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _chunkIdByHash = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, (string HashHex, string DocumentPath)> _chunkRecordByChunkId = new();

    public HashSet<string> GetKnownDocumentPaths()
    {
        return _documentChunkHashes.Keys.ToHashSet();
    }

    public HashSet<string> GetKnownChunkHashes(string documentPath)
    {
        return _documentChunkHashes.TryGetValue(documentPath, out var hashes)
            ? hashes.ToHashSet()
            : [];
    }

    public bool TryGetChunkIdByHash(string hashHex, out int chunkId)
    {
        return _chunkIdByHash.TryGetValue(hashHex, out chunkId);
    }

    public void AddDocument(string path)
    {
        if (!_documentChunkHashes.ContainsKey(path))
        {
            _documentChunkHashes[path] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void AddChunk(string hashHex, int chunkId, string documentPath)
    {
        _chunkIdByHash[hashHex] = chunkId;
        _chunkRecordByChunkId[chunkId] = (hashHex, documentPath);

        if (!_documentChunkHashes.TryGetValue(documentPath, out var hashes))
        {
            hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _documentChunkHashes[documentPath] = hashes;
        }

        hashes.Add(hashHex);
    }

    public void RemoveDocument(string path)
    {
        if (_documentChunkHashes.TryGetValue(path, out var hashes))
        {
            foreach (var hash in hashes)
            {
                if (_chunkIdByHash.Remove(hash, out var chunkId))
                {
                    _chunkRecordByChunkId.Remove(chunkId);
                }
            }
        }

        _documentChunkHashes.Remove(path);
    }

    public void RemoveChunk(string hashHex)
    {
        if (_chunkIdByHash.Remove(hashHex, out var chunkId))
        {
            var record = _chunkRecordByChunkId[chunkId];
            _chunkRecordByChunkId.Remove(chunkId);

            if (_documentChunkHashes.TryGetValue(record.DocumentPath, out var hashes))
            {
                hashes.Remove(hashHex);
            }
        }
    }

    public List<TrackedChunkRecord> ReadAllChunkRecords()
    {
        return _chunkRecordByChunkId
            .Select(kv => new TrackedChunkRecord(kv.Value.HashHex, kv.Key, kv.Value.DocumentPath))
            .ToList();
    }

    // P.S. We expect a very small number of chunks for in-memory usage, but we could upgrade this to be indexed in the future.
    public List<TrackedChunkRecord> ReadAllChunkRecords(string documentPath)
    {
        return _chunkRecordByChunkId
            .Where(kv => kv.Value.DocumentPath == documentPath)
            .Select(kv => new TrackedChunkRecord(kv.Value.HashHex, kv.Key, kv.Value.DocumentPath))
            .ToList();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task PrepareForUseAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
