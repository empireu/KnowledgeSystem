namespace KnowledgeSystem.Api;

/// <summary>
///     Holds information about a single discord message, coming from a user (not included yet).
/// </summary>
/// <param name="Message"></param>
public record struct UserMessageInfo(
    string Message
);