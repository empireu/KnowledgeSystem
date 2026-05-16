using NetCord.Rest;

namespace KnowledgeSystem.Discord;

/// <summary>
///     Abstraction over a Discord message whose content can be updated in-place.
/// </summary>
public interface IDiscordMessageTarget
{
    Task UpdateContentAsync(string content, CancellationToken cancellationToken = default);
    Task SetEmbedAsync(EmbedProperties embed, CancellationToken cancellationToken = default);
}
