namespace KnowledgeSystem.Plugins.Surveillance.Messages;

public sealed class DiscordMessage(DiscordGuild guild, DiscordUser user, DateTime dateTime, ulong messageId, string content)
{
    public DiscordGuild Guild { get; } = guild;

    public DiscordUser User { get; } = user;

    public DateTime DateTime { get; } = dateTime;

    public ulong MessageId { get; } = messageId;

    public string Content { get; } = content;
    
    public DiscordChannel Channel { get; init; }
}