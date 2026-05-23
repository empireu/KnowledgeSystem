using KnowledgeSystem.Agents.Context;

namespace KnowledgeSystem.Agent.Tools.Markers;

public class RepositoryFetchedTextMarker : IMarkerElement
{
    public required string Content { get; init; }
    
    public string ToLogFormat()
    {
        return Content;
    }
}