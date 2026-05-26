using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;

namespace KnowledgeSystem.Agent.Tools.Markers;

public class RepositoryFetchedTextMarker : IMarkerElement
{
    public required EmdDocument Document { get; init; }
    public required List<(int Start, int End)> FetchedRanges { get; init; }
    public required string Content { get; init; }
    
    public string ToLogFormat()
    {
        return Content;
    }
}