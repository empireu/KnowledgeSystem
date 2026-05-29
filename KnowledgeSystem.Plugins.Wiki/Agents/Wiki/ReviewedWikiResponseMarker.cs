using KnowledgeSystem.Agents.Context;

namespace KnowledgeSystem.Plugins.Wiki.Agents.Wiki;

/// <summary>
///     Inserted when the peer-review agent validates a report. 
/// </summary>
public sealed class ReviewedWikiResponseMarker : IMarkerElement
{
    public required string VerifiedReport { get; init; }
    
    public string ToLogFormat()
    {
        return VerifiedReport;
    }
}