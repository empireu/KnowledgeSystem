using KnowledgeSystem.Embedding;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeSystem.Plugins.Surveillance.Database;

/// <summary>
///     Migrates raw <see cref="ExtractedClaimRecord"/> rows to the canonical claims table, resolving entity name strings to canonical entity IDs via the map produced by <see cref="EntityResolver"/>.
///     <para><b>Resolution:</b></para>
///     <list type="bullet">
///         <item><c>SubjectName</c> is resolved against the name->ID map and the <c>canonical_entity_aliases</c> table. Claims whose subject can't be resolved are skipped.</item>
///         <item><c>ObjectEntityName</c> is resolved the same way. If it can't be resolved, the name falls back to <c>object_literal</c> (the claim is still stored but loses its typed object link).</item>
///         <item>Timestamps come from <c>IngestionMessage.Timestamp</c> via the claim's <c>SourceMessageId</c> FK, stored as Unix ms.</item>
///     </list>
///     <para><b>Embeddings (two per claim):</b></para>
///     <list type="bullet">
///         <item><b>raw_embedding</b> - <c>predicate + " " + object_literal + " " + evidence_text</c>. Useful for structured search over modalities, entity names, and raw text.</item>
///         <item><b>sentence_embedding</b> - natural-language sentence form:
///             <c>[SubjectName] predicate "object_literal" (modality)</c> or
///             <c>[SubjectName] predicate [ObjectEntityName] (modality)</c>. Designed for semantic search queries like "discord registration suggestions".</item>
///     </list>
/// </summary>
public sealed class ClaimMigrator(
    IngestionDbContext rawDb,
    CanonicalDbContext canonicalDb,
    IEmbeddingService embeddingService
)
{
    /// <summary>
    ///     Migrate all raw claims to canonical claims, using the name->ID map from entity resolution.
    ///     Returns the number of claims migrated.
    /// </summary>
    public async Task<int> MigrateAsync(Dictionary<string, long> nameToId, CancellationToken cancellationToken)
    {
        var claims = await rawDb.Claims.ToListAsync(cancellationToken);

        if (claims.Count == 0)
        {
            return 0;
        }

        var timestamps = await rawDb.Messages
            .Select(m => new { m.Id, m.Timestamp })
            .ToDictionaryAsync(m => m.Id, m => m.Timestamp, cancellationToken);

        var migrated = 0;
        var rawTexts = new List<(long CanonicalClaimId, string Text)>();
        var sentenceTexts = new List<(long CanonicalClaimId, string Text)>();

        foreach (var claim in claims)
        {
            var subjectId = ResolveEntityId(claim.SubjectName, nameToId);

            if (subjectId == null)
            {
                continue;
            }

            long? objectId = null;
            var objectLiteral = claim.ObjectLiteral;

            if (claim.ObjectEntityName != null)
            {
                objectId = ResolveEntityId(claim.ObjectEntityName, nameToId);

                if (objectId == null)
                {
                    objectLiteral = string.IsNullOrWhiteSpace(objectLiteral)
                        ? claim.ObjectEntityName
                        : objectLiteral;
                }
            }

            if (!timestamps.TryGetValue(claim.SourceMessageId, out var msgTimestamp))
            {
                continue;
            }

            var timestampMs = new DateTimeOffset(msgTimestamp, TimeSpan.Zero).ToUnixTimeMilliseconds();

            var canonicalId = canonicalDb.InsertCanonicalClaim(
                subjectId.Value,
                claim.Predicate,
                objectId,
                objectLiteral,
                claim.Modality,
                claim.EvidenceText,
                claim.SourceMessageId,
                timestampMs
            );

            var rawText = $"{claim.Predicate} {objectLiteral ?? ""} {claim.EvidenceText}";
            rawTexts.Add((canonicalId, rawText));

            var sentence = objectLiteral != null
                ? $"[{claim.SubjectName}] {claim.Predicate} \"{objectLiteral}\" ({claim.Modality})"
                : $"[{claim.SubjectName}] {claim.Predicate} [{claim.ObjectEntityName}] ({claim.Modality})";
          
            sentenceTexts.Add((canonicalId, sentence));

            migrated++;
        }

        if (rawTexts.Count > 0)
        {
            var unique = rawTexts
                .Zip(sentenceTexts, (r, s) => (r.CanonicalClaimId, RawText: r.Text, SentenceText: s.Text))
                .GroupBy(x => x.CanonicalClaimId)
                .Select(g => g.First())
                .ToList();

            var rawEmbeds = await embeddingService.EmbedBatchAsync(unique.Select(x => x.RawText).ToArray(), cancellationToken);
            var sentenceEmbeds = await embeddingService.EmbedBatchAsync(unique.Select(x => x.SentenceText).ToArray(), cancellationToken);

            for (var i = 0; i < unique.Count; i++)
            {
                canonicalDb.InsertClaimEmbeddings(unique[i].CanonicalClaimId, rawEmbeds[i].Span, sentenceEmbeds[i].Span);
            }
        }

        return migrated;
    }

    private long? ResolveEntityId(string name, Dictionary<string, long> nameToId)
    {
        if (nameToId.TryGetValue(name, out var id))
        {
            return id;
        }

        var result = canonicalDb.GetCanonicalEntityByAlias(name);

        if (result != null)
        {
            nameToId[name] = result.Value.Id;
           
            return result.Value.Id;
        }

        return null;
    }
}
