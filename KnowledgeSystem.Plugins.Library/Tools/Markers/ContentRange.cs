using KnowledgeSystem.EmdParser.ExtendedMarkdown;

namespace KnowledgeSystem.Plugins.Library.Tools.Markers;

/// <summary>
///     A structured reference to a region of document content, used by markers for deduplication in the peer review sub-agent.
///     <see cref="Formatted"/> contains the per-range text, suitable for inclusion in the reviewer's context (not guaranteed it will be included, though).
/// </summary>
public readonly record struct ContentRange(
    EmdDocument Document,
    int StartOffset,
    int EndOffset,
    string Formatted
);
