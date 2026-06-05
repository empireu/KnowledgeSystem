using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace KnowledgeSystem.Plugins.Surveillance.Database;

/// <summary>
///     ADO.NET wrapper for the canonical knowledge graph tables.
///     Uses its own <see cref="SqliteConnection"/> to the same SQLite file as <see cref="IngestionDbContext"/>.
///     Allows concurrent reads across connections, but writes don't happen.
///
///     <para><b>Tables managed:</b></para>
///     <list type="bullet">
///         <item><c>canonical_entities</c> + <c>canonical_entity_aliases</c> - deduplicated entity catalog.</item>
///         <item><c>canonical_entities_fts</c> - FTS5 full-text index over entity names and descriptions.</item>
///         <item><c>canonical_claims</c> - resolved claims with canonical entity IDs and Unix ms timestamps.</item>
///         <item><c>canonical_claims_fts</c> - FTS5 index over predicate and object_literal.</item>
///         <item><c>canonical_entity_embeddings</c> - sqlite-vec table for entity name vector search.</item>
///         <item><c>canonical_claim_embeddings</c> - sqlite-vec table with two embedding columns:
///             <c>raw_embedding</c> (predicate + object + evidence) and <c>sentence_embedding</c>
///             (natural-language claim sentence). See <see cref="ClaimMigrator"/> for embedding text construction.</item>
///     </list>
///
///     <para><b>sqlite-vec:</b></para>
///     <para>
///         Vec0 is loaded at <see cref="InitializeSchema"/>. It must be present on disk.
///         Vec0 does not support partial row updates - both embedding columns must be inserted together via <see cref="InsertClaimEmbeddings"/>.
///     </para>
/// </summary>
public sealed class CanonicalDbContext : IDisposable
{
    public SqliteConnection Connection { get; }
    
    public CanonicalDbContext(string dataSource)
    {
        Connection = new SqliteConnection($"Data Source={dataSource}");
        Connection.Open();
    }

    public void Dispose()
    {
        Connection.Dispose();
    }

    /// <summary>
    ///     Loads sqlite-vec and creates all canonical tables, indexes, triggers, and vec0 virtual tables.
    /// </summary>
    public void InitializeSchema()
    {
        Connection.EnableExtensions();

        using var loadCmd = Connection.CreateCommand();
        loadCmd.CommandText = "SELECT load_extension('vec0');";
        loadCmd.ExecuteNonQuery();

        using var cmd = Connection.CreateCommand();
        cmd.CommandText = GetDdl();
        cmd.ExecuteNonQuery();
    }
    
    #region Entity CRUD
    
    public long InsertCanonicalEntity(string primaryName, string? type, string? description)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO canonical_entities (primary_name, type, description, created_at)
            VALUES (@name, @type, @desc, @ts);
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue("@name", primaryName);
        cmd.Parameters.AddWithValue("@type", (object?)type ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        return (long)cmd.ExecuteScalar()!;
    }

    public void AddAlias(long entityId, string alias)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO canonical_entity_aliases (entity_id, alias)
            VALUES (@id, @alias);
            """;

        cmd.Parameters.AddWithValue("@id", entityId);
        cmd.Parameters.AddWithValue("@alias", alias);
        cmd.ExecuteNonQuery();
    }

    public (long Id, string PrimaryName, string? Type, string? Description)? GetCanonicalEntityByAlias(string alias)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            SELECT e.id, e.primary_name, e.type, e.description
            FROM canonical_entities e
            LEFT JOIN canonical_entity_aliases a ON a.entity_id = e.id
            WHERE e.primary_name = @alias COLLATE NOCASE
               OR a.alias = @alias COLLATE NOCASE
            LIMIT 1;
            """;

        cmd.Parameters.AddWithValue("@alias", alias);

        using var reader = cmd.ExecuteReader();

        if (reader.Read())
        {
            return (
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)
            );
        }

        return null;
    }
    
    #endregion

    #region Claim CRUD
    
    public long InsertCanonicalClaim(
        long subjectEntityId,
        string predicate,
        long? objectEntityId,
        string? objectLiteral,
        string modality,
        string evidenceText,
        long sourceMessageId,
        long timestampMs)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO canonical_claims
                (subject_entity_id, predicate, object_entity_id, object_literal, modality, evidence_text, source_message_id, timestamp)
            VALUES (@subj, @pred, @objE, @objL, @mod, @ev, @msgId, @ts);
            """;

        cmd.Parameters.AddWithValue("@subj", subjectEntityId);
        cmd.Parameters.AddWithValue("@pred", predicate);
        cmd.Parameters.AddWithValue("@objE", (object?)objectEntityId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@objL", (object?)objectLiteral ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mod", modality);
        cmd.Parameters.AddWithValue("@ev", evidenceText);
        cmd.Parameters.AddWithValue("@msgId", sourceMessageId);
        cmd.Parameters.AddWithValue("@ts", timestampMs);
        cmd.ExecuteNonQuery();

        using var qCmd = Connection.CreateCommand();
        qCmd.CommandText = """
            SELECT id FROM canonical_claims
            WHERE subject_entity_id = @subj
              AND predicate = @pred
              AND ((object_entity_id = @objE) OR (@objEIsNull AND object_entity_id IS NULL))
              AND ((object_literal = @objL) OR (@objLIsNull AND object_literal IS NULL))
              AND modality = @mod
              AND source_message_id = @msgId
            LIMIT 1;
            """;

        qCmd.Parameters.AddWithValue("@subj", subjectEntityId);
        qCmd.Parameters.AddWithValue("@pred", predicate);
        qCmd.Parameters.AddWithValue("@objE", (object?)objectEntityId ?? DBNull.Value);
        qCmd.Parameters.AddWithValue("@objEIsNull", objectEntityId == null ? 1 : 0);
        qCmd.Parameters.AddWithValue("@objL", (object?)objectLiteral ?? DBNull.Value);
        qCmd.Parameters.AddWithValue("@objLIsNull", objectLiteral == null ? 1 : 0);
        qCmd.Parameters.AddWithValue("@mod", modality);
        qCmd.Parameters.AddWithValue("@msgId", sourceMessageId);

        return (long)qCmd.ExecuteScalar()!;
    }

    #endregion
    
    #region Vector
    
    public void InsertEntityEmbedding(long entityId, ReadOnlySpan<float> embedding)
    {
        var blob = SpanToBlob(embedding);

        using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO canonical_entity_embeddings (entity_id, embedding)
            VALUES (@id, @emb);
            """;

        cmd.Parameters.AddWithValue("@id", entityId);
        cmd.Parameters.AddWithValue("@emb", blob);
        cmd.ExecuteNonQuery();
    }

    public void InsertClaimEmbeddings(long claimId, ReadOnlySpan<float> rawEmbedding, ReadOnlySpan<float> sentenceEmbedding)
    {
        var rawBlob = SpanToBlob(rawEmbedding);
        var sentenceBlob = SpanToBlob(sentenceEmbedding);

        using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO canonical_claim_embeddings (claim_id, raw_embedding, sentence_embedding)
            VALUES (@id, @rawEmb, @sentenceEmb);
            """;

        cmd.Parameters.AddWithValue("@id", claimId);
        cmd.Parameters.AddWithValue("@rawEmb", rawBlob);
        cmd.Parameters.AddWithValue("@sentenceEmb", sentenceBlob);
        cmd.ExecuteNonQuery();
    }

    public List<(long EntityId, float Distance)> SearchEntityEmbeddings(ReadOnlySpan<float> query, int limit = 10)
    {
        var blob = SpanToBlob(query);
        var results = new List<(long, float)>();

        using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            SELECT entity_id, distance
            FROM canonical_entity_embeddings
            WHERE embedding MATCH @query
            ORDER BY distance
            LIMIT @limit;
            """;

        cmd.Parameters.AddWithValue("@query", blob);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            results.Add((reader.GetInt64(0), reader.GetFloat(1)));
        }

        return results;
    }

    public List<(long ClaimId, float Distance)> SearchClaimRawEmbeddings(ReadOnlySpan<float> query, int limit = 10)
    {
        var blob = SpanToBlob(query);
        var results = new List<(long, float)>();

        using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            SELECT claim_id, distance
            FROM canonical_claim_embeddings
            WHERE raw_embedding MATCH @query
            ORDER BY distance
            LIMIT @limit;
            """;

        cmd.Parameters.AddWithValue("@query", blob);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            results.Add((reader.GetInt64(0), reader.GetFloat(1)));
        }

        return results;
    }

    public List<(long ClaimId, float Distance)> SearchClaimSentenceEmbeddings(ReadOnlySpan<float> query, int limit = 10)
    {
        var blob = SpanToBlob(query);
        var results = new List<(long, float)>();

        using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            SELECT claim_id, distance
            FROM canonical_claim_embeddings
            WHERE sentence_embedding MATCH @query
            ORDER BY distance
            LIMIT @limit;
            """;

        cmd.Parameters.AddWithValue("@query", blob);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            results.Add((reader.GetInt64(0), reader.GetFloat(1)));
        }

        return results;
    }

    #endregion
    
    private static byte[] SpanToBlob(ReadOnlySpan<float> values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        MemoryMarshal.AsBytes(values).CopyTo(bytes);
        return bytes;
    }

    private static string GetDdl()
    {
        return """
            CREATE TABLE IF NOT EXISTS canonical_entities (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                primary_name TEXT NOT NULL,
                type TEXT,
                description TEXT,
                created_at INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS canonical_entity_aliases (
                entity_id INTEGER NOT NULL REFERENCES canonical_entities(id) ON DELETE CASCADE,
                alias TEXT NOT NULL,
                PRIMARY KEY (entity_id, alias)
            );

            CREATE INDEX IF NOT EXISTS idx_canonical_entity_aliases_alias
                ON canonical_entity_aliases(alias COLLATE NOCASE);

            CREATE VIRTUAL TABLE IF NOT EXISTS canonical_entities_fts USING fts5(
                primary_name,
                description,
                content=canonical_entities,
                content_rowid=id
            );

            CREATE TRIGGER IF NOT EXISTS canonical_entities_ai AFTER INSERT ON canonical_entities BEGIN
                INSERT INTO canonical_entities_fts(rowid, primary_name, description)
                VALUES (new.id, new.primary_name, new.description);
            END;

            CREATE TRIGGER IF NOT EXISTS canonical_entities_ad AFTER DELETE ON canonical_entities BEGIN
                INSERT INTO canonical_entities_fts(canonical_entities_fts, rowid, primary_name, description)
                VALUES ('delete', old.id, old.primary_name, old.description);
            END;

            CREATE TRIGGER IF NOT EXISTS canonical_entities_au AFTER UPDATE ON canonical_entities BEGIN
                INSERT INTO canonical_entities_fts(canonical_entities_fts, rowid, primary_name, description)
                VALUES ('delete', old.id, old.primary_name, old.description);
                INSERT INTO canonical_entities_fts(rowid, primary_name, description)
                VALUES (new.id, new.primary_name, new.description);
            END;

            CREATE TABLE IF NOT EXISTS canonical_claims (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                subject_entity_id INTEGER NOT NULL REFERENCES canonical_entities(id),
                predicate TEXT NOT NULL,
                object_entity_id INTEGER REFERENCES canonical_entities(id),
                object_literal TEXT,
                modality TEXT NOT NULL,
                evidence_text TEXT NOT NULL,
                source_message_id INTEGER NOT NULL,
                timestamp INTEGER NOT NULL,
                UNIQUE(subject_entity_id, predicate, object_entity_id, object_literal, modality, source_message_id)
            );

            CREATE INDEX IF NOT EXISTS idx_canonical_claims_subject
                ON canonical_claims(subject_entity_id, timestamp);
            CREATE INDEX IF NOT EXISTS idx_canonical_claims_object
                ON canonical_claims(object_entity_id, timestamp);
            CREATE INDEX IF NOT EXISTS idx_canonical_claims_modality_subject
                ON canonical_claims(modality, subject_entity_id);
            CREATE INDEX IF NOT EXISTS idx_canonical_claims_predicate
                ON canonical_claims(predicate);
            CREATE INDEX IF NOT EXISTS idx_canonical_claims_timestamp
                ON canonical_claims(timestamp);

            CREATE VIRTUAL TABLE IF NOT EXISTS canonical_claims_fts USING fts5(
                predicate,
                object_literal,
                content=canonical_claims,
                content_rowid=id
            );

            CREATE TRIGGER IF NOT EXISTS canonical_claims_ai AFTER INSERT ON canonical_claims BEGIN
                INSERT INTO canonical_claims_fts(rowid, predicate, object_literal)
                VALUES (new.id, new.predicate, new.object_literal);
            END;

            CREATE TRIGGER IF NOT EXISTS canonical_claims_ad AFTER DELETE ON canonical_claims BEGIN
                INSERT INTO canonical_claims_fts(canonical_claims_fts, rowid, predicate, object_literal)
                VALUES ('delete', old.id, old.predicate, old.object_literal);
            END;

            CREATE VIRTUAL TABLE IF NOT EXISTS canonical_entity_embeddings USING vec0(
                entity_id INTEGER PRIMARY KEY,
                embedding float[1024]
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS canonical_claim_embeddings USING vec0(
                claim_id INTEGER PRIMARY KEY,
                raw_embedding float[1024],
                sentence_embedding float[1024]
            );
            """;
    }
}
