using KnowledgeSystem.Plugins.Surveillance.Messages;

namespace KnowledgeSystem.Plugins.Surveillance.Extraction;

/// <summary>
///     Maps a line index in a chunk's formatted source content back to the Discord message it came from.
/// </summary>
public sealed class MessageLineMapping
{
    /// <summary>
    ///     The line index in the formatted source content.
    /// </summary>
    public int LineIndex { get; init; }

    /// <summary>
    ///     The Discord message that produced this line.
    /// </summary>
    public DiscordMessage Message { get; init; } = null!;
}
