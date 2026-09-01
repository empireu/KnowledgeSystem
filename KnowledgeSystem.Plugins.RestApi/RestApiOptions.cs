using System.ComponentModel.DataAnnotations;

namespace KnowledgeSystem.Plugins.RestApi;

public class RestApiOptions
{
    public const string Section = "rest_api";

    [Required]
    public ulong? ChannelId { get; set; }

    [Required]
    public string ServerUrl { get; set; } = null!;

    public string? ApiKey { get; set; }
}
