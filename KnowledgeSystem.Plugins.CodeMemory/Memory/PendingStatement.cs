namespace KnowledgeSystem.Plugins.CodeMemory.Memory;

/// <summary>
///     Holds the command uploaded by the harness.
/// </summary>
public record PendingStatement(
    string Namespace,
    string Content,
    DateTime UtcSent
);
