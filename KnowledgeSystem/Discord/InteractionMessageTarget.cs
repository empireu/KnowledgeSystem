using NetCord;
using NetCord.Rest;

namespace KnowledgeSystem.Discord;

/// <summary>
///     Message target that modifies an interaction response.
///     Used for /mqr ask where the response is a deferred interaction.
/// </summary>
public sealed class InteractionMessageTarget : IDiscordMessageTarget
{
    private readonly Interaction _interaction;

    public InteractionMessageTarget(Interaction interaction)
    {
        _interaction = interaction;
    }

    public async Task UpdateContentAsync(string content, CancellationToken cancellationToken = default)
    {
        if (content.Length > 2000)
        {
            content = content[..1997] + "...";
        }

        await _interaction.ModifyResponseAsync(m => m.Content = content, cancellationToken: cancellationToken);
    }

    public async Task SetEmbedAsync(EmbedProperties embed, CancellationToken cancellationToken = default)
    {
        await _interaction.ModifyResponseAsync(m =>
        {
            m.Content = "";
            m.Embeds = [embed];
        }, cancellationToken: cancellationToken);
    }
}
