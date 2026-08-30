namespace KnowledgeSystem.Api;

/// <summary>
///     A file attached to a user's discord message.
/// </summary>
public sealed record UserAttachment(string FileName, string Url, long Size, string? ContentType);
