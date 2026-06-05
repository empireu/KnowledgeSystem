// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace KnowledgeSystem.Plugins.Surveillance.Database;

/// <summary>
///     A batch of messages from a single Discord channel, ingested together.
/// </summary>
public sealed class IngestionBatch
{
    public long Id { get; set; }

    /// <summary>
    ///     The Discord guild ID.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    ///     The Discord guild name at time of export.
    /// </summary>
    public string GuildName { get; set; } = null!;

    /// <summary>
    ///     The Discord channel ID.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    ///     The Discord channel name at time of export.
    /// </summary>
    public string ChannelName { get; set; } = null!;

    /// <summary>
    ///     Timestamp of the earliest message in the batch.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    ///     Timestamp of the latest message in the batch.
    /// </summary>
    public DateTime EndedAt { get; set; }

    /// <summary>
    ///     When this batch record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    ///     The messages in this batch.
    /// </summary>
    public List<IngestionMessage> Messages { get; set; } = [];

    /// <summary>The extraction chunks produced from this batch.</summary>
    public List<IngestionChunk> Chunks { get; set; } = [];
}
