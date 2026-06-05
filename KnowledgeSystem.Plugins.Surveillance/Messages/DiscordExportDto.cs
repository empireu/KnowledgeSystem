using System.Text.Json.Serialization;

// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable ClassNeverInstantiated.Global

namespace KnowledgeSystem.Plugins.Surveillance.Messages;

public class DiscordExportDto
{
    public GuildDto Guild { get; set; } = null!;

    public ChannelDto Channel { get; set; } = null!;

    public List<MessageDto> Messages { get; set; } = [];
}

public class ChannelDto
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public class GuildDto
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public class MessageDto
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong Id { get; set; }

    public DateTime Timestamp { get; set; }
    
    public string? Content { get; set; }
    
    public AuthorDto Author { get; set; } = null!;
}

public class AuthorDto
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong Id { get; set; }

    public string Name { get; set; } = string.Empty;
    
    public string? Nickname { get; set; }
}
