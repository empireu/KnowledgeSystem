using System.Text.Json;

namespace KnowledgeSystem.Plugins.Surveillance.Messages;

public static class DiscordArchiveParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };
    
    public static List<DiscordMessage> Parse(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var export = JsonSerializer.Deserialize<DiscordExportDto>(stream, JsonOptions);

        if (export == null)
        {
            throw new InvalidDataException("Invalid export file");
        }

        var guild = new DiscordGuild(export.Guild.Id, export.Guild.Name);
        var channel = new DiscordChannel(export.Channel.Id, export.Channel.Name);

        var messages = export.Messages
            .Select(m => MapMessage(guild, channel, m))
            .ToList();
        
        messages.Sort((a, b) => a.DateTime.CompareTo(b.DateTime));
        
        return messages;
    }

    private static DiscordMessage MapMessage(DiscordGuild guild, DiscordChannel channel, MessageDto message)
    {
        var user = new DiscordUser(message.Author.Id, message.Author.Name, message.Author.Nickname ?? message.Author.Name);

        return new DiscordMessage(guild, user, message.Timestamp, message.Id, message.Content ?? string.Empty)
        {
            Channel = channel
        };
    }
}
