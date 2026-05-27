using KnowledgeSystem.Agents.Context;

namespace KnowledgeSystem.Plugins.Library.Tools.Markers;

public class FastContextMarker : IMarkerElement
{
    public required string Output { get; init; }
    public required List<ContentRange> Ranges { get; init; }
    
    public string ToLogFormat()
    {
        return Output;
    }
}