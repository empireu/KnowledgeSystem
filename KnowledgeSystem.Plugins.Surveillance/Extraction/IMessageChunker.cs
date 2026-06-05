using KnowledgeSystem.Plugins.Surveillance.Messages;

namespace KnowledgeSystem.Plugins.Surveillance.Extraction;

/// <summary>
///     Splits a list of Discord messages into chunks suitable for the extraction pipeline.
/// </summary>
public interface IMessageChunker
{
    /// <summary>
    ///     Chunk messages into groups that respect temporal coherence and a character limit.
    /// </summary>
    List<ChunkedMessages> Chunk(List<DiscordMessage> messages);
}
