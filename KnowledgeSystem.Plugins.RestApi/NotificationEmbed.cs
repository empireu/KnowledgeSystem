namespace KnowledgeSystem.Plugins.RestApi;

public sealed class NotificationEmbed
{
    public string? Title { get; set; }

    public string? Description { get; set; }

    public uint? Color { get; set; }

    public List<NotificationEmbedField>? Fields { get; set; }
}
