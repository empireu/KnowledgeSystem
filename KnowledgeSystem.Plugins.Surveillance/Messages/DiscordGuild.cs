namespace KnowledgeSystem.Plugins.Surveillance.Messages;

public readonly struct DiscordGuild(ulong guildId, string name)
{
    public ulong GuildId { get; } = guildId;
    
    public string Name { get; } = name;
}