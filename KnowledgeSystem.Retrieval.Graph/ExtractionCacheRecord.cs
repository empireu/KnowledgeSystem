using KnowledgeSystem.Retrieval.Api.Graph;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace KnowledgeSystem.Retrieval.Graph;

public sealed class ExtractionCacheRecord
{
    public string? SourceContent { get; init; }

    public CacheEntity[] Entities { get; init; } = [];

    public CacheRelationship[] Relationships { get; init; } = [];

    public CacheAttribute[] Attributes { get; init; } = [];
}

public sealed class CacheEntity
{
    public required string Name { get; init; }
    public string[]? Names { get; init; }
    public string? Type { get; init; }
    public string? Description { get; init; }
    public CacheEvidence? Evidence { get; init; }
}

public sealed class CacheRelationship
{
    public required string SourceName { get; init; }
    public required string TargetName { get; init; }
    public required string Description { get; init; }
    public CacheEvidence? Evidence { get; init; }
}

public sealed class CacheAttribute
{
    public required string EntityName { get; init; }
    public required string AttributeName { get; init; }
    public required string Value { get; init; }
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
            SourceContent = chunk.Source.Content,
            Entities = chunk.Entities.Select(e => new CacheEntity
            {
                Name = e.DefinedNames[0],
                Names = e.DefinedNames.Length > 1 ? e.DefinedNames : null,
                Type = e.Type,
                Description = e.Description,
                Evidence = ToCache(e.Evidence)
            }).ToArray(),
            Relationships = chunk.Relationships.Select(r => new CacheRelationship
            {
                SourceName = r.Source.DefinedNames[0],
                TargetName = r.Target.DefinedNames[0],
                Description = r.ActionDescription,
                Evidence = ToCache(r.Evidence)
            }).ToArray(),
            Attributes = chunk.Attributes.Select(a => new CacheAttribute
            {
                EntityName = a.EntityName,
                AttributeName = a.AttributeName,
                Value = a.Value,
                Evidence = ToCache(a.Evidence)
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
