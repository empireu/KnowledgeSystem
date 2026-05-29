using KnowledgeSystem.Plugins.Library;

namespace KnowledgeSystem.Plugins.Wiki.Memory;


/// <summary>
///     Holds the timeline of a finished wiki chat.
/// </summary>
public record PendingChat(BasicContext Context, DateTime UtcStarted, DateTime UtcFinished);
