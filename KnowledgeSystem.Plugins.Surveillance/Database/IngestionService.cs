using System.Text.Json;
using KnowledgeSystem.Plugins.Surveillance.Extraction;
using KnowledgeSystem.Plugins.Surveillance.Messages;
using KnowledgeSystem.Retrieval.Api.Graph;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeSystem.Plugins.Surveillance.Database;

/// <summary>
///     Ingests Discord messages into the database by chunking, running the extraction pipeline, and storing results.
/// </summary>
public sealed class IngestionService(IngestionDbContext db, IMessageChunker chunker)
{
    /// <summary>
    ///     Ingest a batch of Discord messages from a single channel.
    ///     Messages already present in the database for this channel are skipped.
    /// </summary>
    public async Task<IngestionBatch?> IngestAsync(List<DiscordMessage> messages, IFeatureExtractionPipeline pipeline, CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return null;
        }

        var newMessages = await DedupMessagesAsync(messages, cancellationToken);

        if (newMessages.Count == 0)
        {
            return null;
        }

        var first = messages[0];
        
        var batch = new IngestionBatch
        {
            GuildId = first.Guild.GuildId,
            GuildName = first.Guild.Name,
            ChannelId = first.Channel.ChannelId,
            ChannelName = first.Channel.Name,
            StartedAt = messages[0].DateTime,
            EndedAt = messages[^1].DateTime,
            CreatedAt = DateTime.UtcNow
        };

        db.Batches.Add(batch);

        foreach (var message in newMessages)
        {
            db.Messages.Add(new IngestionMessage
            {
                Batch = batch,
                MessageId = message.MessageId,
                UserId = message.User.UserId,
                Username = message.User.Username,
                Nickname = message.User.Nickname,
                Content = NormalizeLineEndings(message.Content),
                Timestamp = message.DateTime
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        var messageIdToDbId = await db.Messages
            .Where(m => m.BatchId == batch.Id)
            .ToDictionaryAsync(m => m.MessageId, m => m.Id, cancellationToken);

        var chunks = chunker.Chunk(newMessages);

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
          
            var dbChunk = new IngestionChunk
            {
                Batch = batch,
                Sequence = i,
                StartedAt = chunk.StartedAt,
                EndedAt = chunk.EndedAt,
                Status = ChunkStatus.Pending
            };

            db.Chunks.Add(dbChunk);
        }

        await db.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = chunks[i];
            var dbChunk = batch.Chunks[i];
            var source = new IngestionChunkSource(chunk.SourceContent);
            var result = await pipeline.IngestAsync(source, cancellationToken);

            foreach (var entity in result.Entities)
            {
                db.Entities.Add(new ExtractedEntityRecord
                {
                    Chunk = dbChunk,
                    Name = entity.DefinedNames[0],
                    AliasesJson = SerializeAliases(entity.DefinedNames),
                    Type = entity.Type,
                    Description = entity.Description,
                    EvidenceText = entity.Evidence.QuotedText,
                    SourceMessageId = ResolveSourceMessageId(chunk, entity.Evidence.Span, messageIdToDbId)
                });
            }

            foreach (var claim in result.Claims)
            {
                db.Claims.Add(new ExtractedClaimRecord
                {
                    Chunk = dbChunk,
                    SubjectName = claim.Subject.DefinedNames[0],
                    Predicate = claim.Predicate,
                    ObjectEntityName = claim.ObjectEntity?.DefinedNames[0],
                    ObjectLiteral = claim.ObjectLiteral,
                    Modality = claim.Modality,
                    EvidenceText = claim.Evidence.QuotedText,
                    SourceMessageId = ResolveSourceMessageId(chunk, claim.Evidence.Span, messageIdToDbId)
                });
            }

            dbChunk.Status = ChunkStatus.Completed;
            await db.SaveChangesAsync(cancellationToken);
        }

        return batch;
    }

    /// <summary>
    ///     Dedups the messages against the database.
    /// </summary>
    private async Task<List<DiscordMessage>> DedupMessagesAsync(List<DiscordMessage> messages, CancellationToken cancellationToken)
    {
        var channelId = messages[0].Channel.ChannelId;
        
        var knownIds = await db.Messages
            .Where(m => m.Batch.ChannelId == channelId)
            .Select(m => m.MessageId)
            .ToHashSetAsync(cancellationToken);

        return messages.Where(m => !knownIds.Contains(m.MessageId)).ToList();
    }

    private static long ResolveSourceMessageId(ChunkedMessages chunk, TextSpan? span, Dictionary<ulong, long> messageIdToDbId)
    {
        var lineIndex = 0;

        if (span.HasValue)
        {
            var end = Math.Min(span.Value.Start, chunk.SourceContent.Length);
            var newlineCount = 0;

            for (var i = 0; i < end; i++)
            {
                if (chunk.SourceContent[i] == '\n')
                {
                    newlineCount++;
                }
            }

            lineIndex = newlineCount;
        }

        if (lineIndex >= chunk.LineMappings.Count)
        {
            lineIndex = chunk.LineMappings.Count - 1;
        }

        if (lineIndex < 0)
        {
            lineIndex = 0;
        }

        var messageId = chunk.LineMappings[lineIndex].Message.MessageId;
        return messageIdToDbId[messageId];
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static string SerializeAliases(string[] definedNames)
    {
        if (definedNames.Length <= 1)
        {
            return "[]";
        }

        return JsonSerializer.Serialize(definedNames[1..]);
    }
}
