using NetCord.Rest;

namespace KnowledgeSystem.Discord;

/// <summary>
///     Message target that modifies a message sent in a channel.
///     Used for thread conversations where we send a message and then edit it.
/// </summary>
public sealed class ChannelMessageTarget : IDiscordMessageTarget
{
    private readonly RestClient _restClient;
    private readonly ulong _channelId;
    private readonly ulong _messageId;

    public ChannelMessageTarget(RestClient restClient, ulong channelId, ulong messageId)
    {
        _restClient = restClient;
        _channelId = channelId;
        _messageId = messageId;
    }

    public async Task UpdateContentAsync(string content, CancellationToken cancellationToken = default)
    {
        if (content.Length > 2000)
        {
            content = content[..1997] + "...";
        }

        await _restClient.ModifyMessageAsync(_channelId, _messageId, m => m.Content = content, cancellationToken: cancellationToken);
    }

    public async Task SetEmbedAsync(EmbedProperties embed, CancellationToken cancellationToken = default)
    {
        await _restClient.ModifyMessageAsync(_channelId, _messageId, m =>
        {
            m.Content = "";
            m.Embeds = [embed];
        }, cancellationToken: cancellationToken);
    }
}
