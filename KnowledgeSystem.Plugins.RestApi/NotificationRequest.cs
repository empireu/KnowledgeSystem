namespace KnowledgeSystem.Plugins.RestApi;

public sealed class NotificationRequest
{
    public string? Content { get; set; }

    public NotificationEmbed? Embed { get; set; }
}
