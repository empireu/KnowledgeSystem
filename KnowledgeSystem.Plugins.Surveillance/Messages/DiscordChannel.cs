namespace KnowledgeSystem.Plugins.Surveillance.Messages;

public readonly struct DiscordChannel(ulong channelId, string name)
{
    public ulong ChannelId { get; } = channelId;

    public string Name { get; } = name;
}
