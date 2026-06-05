using System.Text;
using KnowledgeSystem.Plugins.Surveillance.Messages;

namespace KnowledgeSystem.Plugins.Surveillance.Extraction;

/// <summary>
///     Two-pass chunker.
///     First buckets messages by temporal coherence, then splits buckets by a character limit.
/// </summary>
public sealed class MessageChunker(TimeSpan? temporalGapThreshold = null, int maxChunkCharacters = 65536) : IMessageChunker
{
    private readonly TimeSpan _temporalGapThreshold = temporalGapThreshold ?? TimeSpan.FromMinutes(30);

    public List<ChunkedMessages> Chunk(List<DiscordMessage> messages)
    {
        if (messages.Count == 0)
        {
            return [];
        }

        var temporalBuckets = BucketByTime(messages);
        var chunks = new List<ChunkedMessages>();

        foreach (var bucket in temporalBuckets)
        {
            chunks.AddRange(SplitByCharacterLimit(bucket));
        }

        return chunks;
    }

    private List<List<DiscordMessage>> BucketByTime(List<DiscordMessage> messages)
    {
        var buckets = new List<List<DiscordMessage>>();

        var currentBucket = new List<DiscordMessage>
        {
            messages[0]
        };

        for (var i = 1; i < messages.Count; i++)
        {
            var gap = messages[i].DateTime - messages[i - 1].DateTime;

            if (gap > _temporalGapThreshold)
            {
                buckets.Add(currentBucket);
                currentBucket = [];
            }

            currentBucket.Add(messages[i]);
        }

        buckets.Add(currentBucket);
        return buckets;
    }

    private List<ChunkedMessages> SplitByCharacterLimit(List<DiscordMessage> bucket)
    {
        var chunks = new List<ChunkedMessages>();
        var sb = new StringBuilder();
        var lineMappings = new List<MessageLineMapping>();
        var currentLineIndex = 0;
        var currentCharCount = 0;
        var chunkStartIndex = 0;

        for (var i = 0; i < bucket.Count; i++)
        {
            var message = bucket[i];
            var line = $"<{message.User.Nickname}> {NormalizeLineEndings(message.Content)}\n";
            var lineLength = line.Length;

            if (currentCharCount + lineLength > maxChunkCharacters && currentCharCount > 0)
            {
                chunks.Add(FinalizeChunk(sb, lineMappings, bucket, chunkStartIndex, i - 1));
                sb.Clear();
                lineMappings.Clear();
                currentCharCount = 0;
                chunkStartIndex = i;
            }

            sb.Append(line);
            
            lineMappings.Add(new MessageLineMapping
            {
                LineIndex = currentLineIndex, Message = message
            });
            
            currentCharCount += lineLength;
            currentLineIndex++;
        }

        if (currentCharCount > 0)
        {
            chunks.Add(FinalizeChunk(sb, lineMappings, bucket, chunkStartIndex, bucket.Count - 1));
        }

        return chunks;
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static ChunkedMessages FinalizeChunk(
        StringBuilder sb,
        List<MessageLineMapping> mappings,
        List<DiscordMessage> bucket,
        int startIndex,
        int endIndex)
    {
        return new ChunkedMessages
        {
            SourceContent = sb.ToString(),
            LineMappings = mappings,
            StartedAt = bucket[startIndex].DateTime,
            EndedAt = bucket[endIndex].DateTime
        };
    }
}
