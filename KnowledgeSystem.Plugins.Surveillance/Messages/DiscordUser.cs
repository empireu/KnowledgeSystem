namespace KnowledgeSystem.Plugins.Surveillance.Messages;

public readonly struct DiscordUser(ulong userId, string username, string nickname)
{
    public ulong UserId { get; } = userId;
    
    public string Username { get; } = username;
    
    public string Nickname { get; } = nickname;
}