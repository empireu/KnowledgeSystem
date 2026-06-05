// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace KnowledgeSystem.Plugins.Surveillance.Database;

/// <summary>
///     A single Discord message stored as part of an ingestion batch.
/// </summary>
public sealed class IngestionMessage
{
    public long Id { get; set; }

    /// <summary>
    ///     The batch this message belongs to.
    /// </summary>
    public long BatchId { get; set; }

    /// <summary>
    ///     The Discord message ID.
    /// </summary>
    public ulong MessageId { get; set; }

    /// <summary>
    ///     The Discord user ID of the author.
    /// </summary>
    public ulong UserId { get; set; }

    /// <summary>
    ///     The author's username at time of export.
    /// </summary>
    public string Username { get; set; } = null!;

    /// <summary>
    ///     The author's server nickname at time of export.
    /// </summary>
    public string Nickname { get; set; } = null!;

    /// <summary>
    ///     The message text content.
    /// </summary>
    public string Content { get; set; } = null!;

    /// <summary>
    ///     When the message was sent.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    ///     The batch this message belongs to.
    /// </summary>
    public IngestionBatch Batch { get; set; } = null!;
}
