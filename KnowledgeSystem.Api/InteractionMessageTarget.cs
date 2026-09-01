using NetCord;
using NetCord.Rest;

namespace KnowledgeSystem.Api;

/// <summary>
///     Message target that modifies an interaction response.
///     Used for /mqr ask where the response is a deferred interaction.
/// </summary>
public sealed class InteractionMessageTarget(Interaction interaction) : IDiscordMessageTarget
{
    public async Task UpdateContentAsync(string content, CancellationToken cancellationToken = default)
    {
        if (content.Length > 2000)
        {
            content = TruncateAtLineBoundary(content, 1997);
        }

        await interaction.ModifyResponseAsync(m => m.Content = content, cancellationToken: cancellationToken);
    }
    
    /// <summary>
    ///     Truncates at a line boundary so the message content never cuts through a markdown thing.
    /// </summary>
    private static string TruncateAtLineBoundary(string content, int maxLength)
    {
        var newline = content.LastIndexOf('\n', maxLength - 1);

        if (newline > 0)
        {
            return content[..newline] + "...";
        }

        return content[..maxLength] + "...";
    }

    public async Task SetEmbedAsync(EmbedProperties embed, IReadOnlyList<MessageAttachment>? attachments, CancellationToken cancellationToken = default)
    {
        await interaction.ModifyResponseAsync(m =>
        {
            m.Content = "";
            m.Embeds = [embed];
            m.Attachments = attachments?.Select(a => new AttachmentProperties(a.FileName, new MemoryStream(a.Content))).ToList();
        }, cancellationToken: cancellationToken);
    }

    public override string ToString()
    {
        return $"{nameof(InteractionMessageTarget)}[{interaction.User.Username}, {interaction.Channel}, {interaction.Id}]";
    }
}
