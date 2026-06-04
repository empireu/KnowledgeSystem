using KnowledgeSystem.Retrieval.Api.Graph;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace KnowledgeSystem.Retrieval.Graph;

public sealed class ExtractionCacheRecord
{
    public string? SourceContent { get; init; }

    public CacheEntity[] Entities { get; init; } = [];

    public CacheClaim[] Claims { get; init; } = [];
}

public sealed class CacheEntity
{
    public required string Name { get; init; }
    public string[]? Names { get; init; }
    public string? Type { get; init; }
    public string? Description { get; init; }
    public CacheEvidence? Evidence { get; init; }
}

public sealed class CacheClaim
{
    public required string SubjectName { get; init; }
    public required string Predicate { get; init; }
    public string? ObjectEntityName { get; init; }
    public string? ObjectLiteral { get; init; }
    public required string Modality { get; init; }
    public CacheEvidence? Evidence { get; init; }
}

public sealed class CacheEvidence
{
    public required string QuotedText { get; init; }
    public CacheTextSpan? Span { get; init; }
}

public sealed class CacheTextSpan
{
    public int Start { get; init; }
    public int Length { get; init; }
}

public static class ExtractionCacheConvert
{
    public static ExtractionCacheRecord ToCache(this RawProcessedIngestionChunk chunk)
    {
        return new ExtractionCacheRecord
        {
            SourceContent = chunk.SourceContent,
            Entities = chunk.Entities.Select(entity => new CacheEntity
            {
                Name = entity.DefinedNames[0],
                Names = entity.DefinedNames.Length > 1 ? entity.DefinedNames : null,
                Type = entity.Type,
                Description = entity.Description,
                Evidence = ToCache(entity.Evidence)
            }).ToArray(),
            Claims = chunk.Claims.Select(claim => new CacheClaim
            {
                SubjectName = claim.Subject.DefinedNames[0],
                Predicate = claim.Predicate,
                ObjectEntityName = claim.ObjectEntity?.DefinedNames[0],
                ObjectLiteral = claim.ObjectLiteral,
                Modality = claim.Modality,
                Evidence = ToCache(claim.Evidence)
            }).ToArray()
        };
    }

    private static CacheEvidence ToCache(RawEvidence evidence)
    {
        return new CacheEvidence
        {
            QuotedText = evidence.QuotedText,
            Span = evidence.Span.HasValue
                ? new CacheTextSpan { Start = evidence.Span.Value.Start, Length = evidence.Span.Value.Length }
                : null
        };
    }
}
