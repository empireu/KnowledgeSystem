using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using KnowledgeSystem.Lexical;
using KnowledgeSystem.Retrieval.Api.Graph;
using Microsoft.Extensions.AI;
// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable LoopCanBeConvertedToQuery
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace KnowledgeSystem.Retrieval.Graph;

/// <summary>
///     Single-LLM-call (when successful) for ingesting the chunk.
/// </summary>
public sealed class BasicOneShotFeatureExtractionPipeline(BasicOneShotFeatureExtractionPipelineDescription description) : IFeatureExtractionPipeline
{
    /// <summary>
    ///     The JSON schema for extraction.
    ///     Entities have a primary <c>name</c> and optional <c>names</c> array for all aliases found in this chunk.
    ///     Relationships reference entities by their <c>name</c> string (resolved to <see cref="RawExtractedEntity"/> in <see cref="IngestAsync"/>).
    /// </summary>
    public static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            entities = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string" },
                        names = new
                        {
                            type = "array",
                            items = new { type = "string" }
                        },
                        type = new { type = "string" },
                        description = new { type = "string" },
                        evidence = new { type = "string" }
                    },
                    required = new[] { "name", "evidence" }
                }
            },
            relationships = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        source = new { type = "string" },
                        target = new { type = "string" },
                        description = new { type = "string" },
                        evidence = new { type = "string" }
                    },
                    required = new[] { "source", "target", "description", "evidence" }
                }
            },
            attributes = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        entity = new { type = "string" },
                        name = new { type = "string" },
                        value = new { type = "string" },
                        evidence = new { type = "string" }
                    },
                    required = new[] { "entity", "name", "value", "evidence" }
                }
            }
        },
        required = new[] { "entities", "relationships", "attributes" }
    });

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<RawProcessedIngestionChunk> IngestAsync(IngestionChunkSource source, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>(2)
        {
            new(ChatRole.System, description.SystemPrompt),
            new(ChatRole.User, source.Content)
        };
        
        var options = description.StructuredCompletionFactory?.Invoke() ?? new ChatOptions();
        options.ResponseFormat = ChatResponseFormat.Json;

        var response = await description.ChatClient.GetResponseAsync(messages, options, cancellationToken);
        var text = response.Text;

        var extracted = JsonSerializer.Deserialize<ExtractionJsonResult>(text, JsonOptions) 
                        ?? throw new InvalidOperationException("Deserialization failed");

        // Fuzzy-match evidence. If everything matches, accept:
        if (!CheckAllEvidence(source.Content, extracted))
        {
            throw new Exception("Extraction failed: could not match all evidence");
        }

        // Build entity map:
        var entityMap = new Dictionary<string, RawExtractedEntity>(StringComparer.OrdinalIgnoreCase);
        var entities = new List<RawExtractedEntity>(extracted.Entities.Length);

        foreach (var jsonEntity in extracted.Entities)
        {
            var names = jsonEntity.Names?.Length > 0
                ? jsonEntity.Names
                : [jsonEntity.Name];

            var evidence = MatchEvidence(source.Content, jsonEntity.Evidence);
            var entity = new RawExtractedEntity(evidence, names, jsonEntity.Description, jsonEntity.Type);

            entityMap[jsonEntity.Name] = entity;
            entities.Add(entity);
        }

        // Build relationships, resolving entity names to objects:
        var relationships = new List<RawEntityApplication>(extracted.Relationships.Length);

        foreach (var jsonRelationship in extracted.Relationships)
        {
            if (!TryResolveEntity(entityMap, jsonRelationship.Source, out var sourceEntity))
            {
                throw new Exception($"LLM created relationship with source \"{jsonRelationship.Source}\", which was not matched.");
            }

            if (!TryResolveEntity(entityMap, jsonRelationship.Target, out var targetEntity))
            {
                throw new Exception($"LLM created relationship with target \"{jsonRelationship.Target}\", which was not matched.");
            }

            var evidence = MatchEvidence(source.Content, jsonRelationship.Evidence);
            
            var relationship = new RawEntityApplication(
                evidence, 
                sourceEntity,
                targetEntity,
                jsonRelationship.Description
            );

            relationships.Add(relationship);
        }

        // Build attributes:
        var attributes = new List<RawEntityAttribute>(extracted.Attributes.Length);

        foreach (var jsonAttribute in extracted.Attributes)
        {
            var evidence = MatchEvidence(source.Content, jsonAttribute.Evidence);
            var attribute = new RawEntityAttribute(
                evidence,
                jsonAttribute.Entity,
                jsonAttribute.Name,
                jsonAttribute.Value
            );

            attributes.Add(attribute);
        }

        return new RawProcessedIngestionChunk(source)
        {
            Entities = entities.ToArray(),
            Relationships = relationships.ToArray(),
            Attributes = attributes.ToArray()
        };
    }

    /// <summary>
    ///     Resolves an entity name by exact match first, then by substring/alias fallback.
    ///     Handles cases where the LLM uses a shortened name (e.g. "Miller" instead of "Detective Miller").
    /// </summary>
    private static bool TryResolveEntity(
        Dictionary<string, RawExtractedEntity> entityMap,
        string name,
        [NotNullWhen(true)] out RawExtractedEntity? entity)
    {
        if (entityMap.TryGetValue(name, out entity))
        {
            return true;
        }

        foreach (var (_, candidate) in entityMap)
        {
            // Check all known names for this entity:
            var allNames = candidate.DefinedNames;
            for (var i = 0; i < allNames.Length; i++)
            {
                if (allNames[i].Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(allNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    entity = candidate;
                    return true;
                }
            }
        }

        entity = null;
        return false;
    }

    /// <summary>
    ///     Returns true only if every evidence field in the result can be fuzzy-matched to the source text.
    /// </summary>
    private bool CheckAllEvidence(string content, ExtractionJsonResult extracted)
    {
        for (var index = 0; index < extracted.Entities.Length; index++)
        {
            var entity = extracted.Entities[index];
          
            if (!IsEvidenceMatch(content, entity.Evidence))
            {
                return false;
            }
        }

        for (var index = 0; index < extracted.Relationships.Length; index++)
        {
            var relationship = extracted.Relationships[index];
            if (!IsEvidenceMatch(content, relationship.Evidence))
            {
                return false;
            }
        }

        for (var index = 0; index < extracted.Attributes.Length; index++)
        {
            var attribute = extracted.Attributes[index];
            if (!IsEvidenceMatch(content, attribute.Evidence))
            {
                return false;
            }
        }

        return true;
    }
    
    private bool IsEvidenceMatch(string content, string evidenceText)
    {
        if (string.IsNullOrEmpty(evidenceText))
        {
            return true;
        }

        // Exact match is always accepted
        if (content.Contains(evidenceText, StringComparison.Ordinal))
        {
            return true;
        }

        var evidenceSentences = SplitSentences(evidenceText);
        if (evidenceSentences.Count == 0)
        {
            return true;
        }

        var sourceSentences = SplitSentences(content);

        var matchedSentences = 0;
        foreach (var (sentence, _, _) in evidenceSentences)
        {
            if (content.Contains(sentence, StringComparison.Ordinal))
            {
                matchedSentences++;
                continue;
            }

            var evidenceTokens = TokenizeToLower(sentence);
            if (evidenceTokens.Count == 0)
            {
                matchedSentences++;
                continue;
            }

            var bestOverlap = sourceSentences.Max(s => TokenOverlap(evidenceTokens, TokenizeToLower(s.Text)));
            if (bestOverlap >= description.MatchThreshold)
            {
                matchedSentences++;
            }
        }

        return (double)matchedSentences / evidenceSentences.Count >= description.MatchThreshold;
    }

    /// <summary>
    ///     Matches evidence text to source content, producing a <see cref="RawEvidence"/> with best-effort offsets.
    ///     Splits evidence into individual sentences and finds the best-matching span for the whole.
    ///     Falls back to null <see cref="RawEvidence.Span"/> if no sentence clears the threshold.
    /// </summary>
    private RawEvidence MatchEvidence(string content, string evidenceText)
    {
        if (string.IsNullOrEmpty(evidenceText))
        {
            return new RawEvidence(evidenceText, content, null);
        }

        var index = content.IndexOf(evidenceText, StringComparison.Ordinal);
        if (index >= 0)
        {
            return new RawEvidence(evidenceText, content, new TextSpan(index, evidenceText.Length));
        }

        var evidenceSentences = SplitSentences(evidenceText);
        var sourceSentences = SplitSentences(content);

        if (evidenceSentences.Count == 0)
        {
            return new RawEvidence(evidenceText, content, null);
        }

        // Find the best-matching source sentence across all evidence sentences:
        (string Text, int Start, int Length)? bestSourceSentence = null;
        var bestScore = 0.0;

        foreach (var (evidenceSentence, _, _) in evidenceSentences)
        {
            var evidenceTokens = TokenizeToLower(evidenceSentence);
            if (evidenceTokens.Count == 0)
            {
                continue;
            }

            for (var i = 0; i < sourceSentences.Count; i++)
            {
                var (sourceText, start, length) = sourceSentences[i];
                var score = TokenOverlap(evidenceTokens, TokenizeToLower(sourceText));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestSourceSentence = (sourceText, start, length);
                }
            }
        }

        if (bestSourceSentence != null && bestScore >= description.MatchThreshold)
        {
            var (_, start, length) = bestSourceSentence.Value;
            return new RawEvidence(evidenceText, content, new TextSpan(start, length));
        }

        return new RawEvidence(evidenceText, content, null);
    }
    
    private static double TokenOverlap(HashSet<string> evidenceTokens, HashSet<string> candidateTokens)
    {
        if (evidenceTokens.Count == 0)
        {
            return 1.0;
        }

        var intersection = 0;
        foreach (var token in evidenceTokens)
        {
            if (candidateTokens.Contains(token))
            {
                intersection++;
            }
        }

        return (double)intersection / evidenceTokens.Count;
    }
    
    private static HashSet<string> TokenizeToLower(string text)
    {
        var tokens = Tokenizer.TokenizeQuery(text, false);
       
        return tokens.Length == 0 
            ? []
            : new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Splits text into sentences, returning each with its byte offset and length.
    ///     Splits on sentence-ending punctuation (. ! ?) followed by whitespace or end.
    /// </summary>
    internal static List<(string Text, int Start, int Length)> SplitSentences(string text)
    {
        var sentences = new List<(string Text, int Start, int Length)>();
        var span = text.AsSpan();
        var sentenceStart = 0;

        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] is not ('.' or '!' or '?'))
            {
                continue;
            }

            if (i + 1 < span.Length && !char.IsWhiteSpace(span[i + 1]) && span[i + 1] != '\n')
            {
                continue;
            }

            var sentenceEnd = i + 1;
            
            // Trim trailing whitespace:
            while (sentenceEnd > sentenceStart && char.IsWhiteSpace(span[sentenceEnd - 1]))
            {
                sentenceEnd--;
            }

            if (sentenceEnd > sentenceStart)
            {
                sentences.Add((span[sentenceStart..sentenceEnd].ToString(), sentenceStart, sentenceEnd - sentenceStart));
            }

            i++;
            while (i < span.Length && char.IsWhiteSpace(span[i]))
            {
                i++;
            }

            sentenceStart = i;
            i--;
        }

        // Last sentence if any content remains:
        if (sentenceStart < span.Length)
        {
            var end = span.Length;
            while (end > sentenceStart && char.IsWhiteSpace(span[end - 1]))
            {
                end--;
            }

            if (end > sentenceStart)
            {
                sentences.Add((span[sentenceStart..end].ToString(), sentenceStart, end - sentenceStart));
            }
        }

        return sentences;
    }

    internal sealed class ExtractionJsonResult
    {
        public JsonEntity[] Entities { get; set; } = [];
        
        public JsonRelationship[] Relationships { get; set; } = [];
        
        public JsonAttribute[] Attributes { get; set; } = [];
    }

    internal sealed class JsonEntity
    {
        public required string Name { get; set; }
        public string[]? Names { get; set; }
        public string? Type { get; set; }
        public string? Description { get; set; }
        public required string Evidence { get; set; }
    }

    internal sealed class JsonRelationship
    {
        public required string Source { get; set; }
        public required string Target { get; set; }
        public required string Description { get; set; }
        public required string Evidence { get; set; }
    }

    internal sealed class JsonAttribute
    {
        public required string Entity { get; set; }
        public required string Name { get; set; }
        public required string Value { get; set; }
        public required string Evidence { get; set; }
    }
}