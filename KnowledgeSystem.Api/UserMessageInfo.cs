using NetCord;

namespace KnowledgeSystem.Api;

/// <summary>
///     Holds information about a single discord message, coming from a user (not included yet).
/// </summary>
public readonly struct UserMessageInfo(string message, string username, IReadOnlyList<UserAttachment>? attachments = null)
{
    public string Message { get; } = message;
    
    public string Username { get; } = username;

    public IReadOnlyList<UserAttachment> Attachments { get; } = attachments ?? [];
}