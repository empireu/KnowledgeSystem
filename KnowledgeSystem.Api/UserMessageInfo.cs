using NetCord;

namespace KnowledgeSystem.Api;

/// <summary>
///     Holds information about a single discord message, coming from a user (not included yet).
/// </summary>
public readonly struct UserMessageInfo(string message)
{
    public string Message { get; } = message;
}