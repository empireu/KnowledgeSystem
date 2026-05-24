using KnowledgeSystem.Agents.Context;

namespace KnowledgeSystem.Agent.Tools.Markers;

public class GrepContentMarker : IMarkerElement
{
    public required string Output { get; init; }
    
    public string ToLogFormat() => Output;
}
