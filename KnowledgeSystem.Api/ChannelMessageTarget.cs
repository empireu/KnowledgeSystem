using NetCord.Rest;

namespace KnowledgeSystem.Api;

/// <summary>
///     Message target that modifies a message sent in a channel.
///     Used for thread conversations where we send a message and then edit it.
/// </summary>
public sealed class ChannelMessageTarget(RestClient restClient, ulong channelId, ulong messageId) : IDiscordMessageTarget
{
    public async Task UpdateContentAsync(string content, CancellationToken cancellationToken = default)
    {
        if (content.Length > 1997)
        {
            content = content[..1997] + "...";
        }

        await restClient.ModifyMessageAsync(channelId, messageId, m => m.Content = content, cancellationToken: cancellationToken);
    }

    public async Task SetEmbedAsync(EmbedProperties embed, IReadOnlyList<MessageAttachment>? attachments, CancellationToken cancellationToken = default)
    {
        await restClient.ModifyMessageAsync(channelId, messageId, m =>
        {
            m.Content = "";
            m.Embeds = [embed];
            m.Attachments = attachments?.Select(a => new AttachmentProperties(a.FileName, new MemoryStream(a.Content))).ToList();
        }, cancellationToken: cancellationToken);
    }
    
    public override string ToString()
    {
        return $"{nameof(ChannelMessageTarget)}[{channelId}]";
    }
}
