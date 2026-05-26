using System.Text;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;

namespace KnowledgeSystem.Agent.Tools.Markers;

public class RepositoryFetchedNodeMarker : IMarkerElement
{
    public required EmdNode Node { get; init; }
    
    public string ToLogFormat()
    {
        var sb = new StringBuilder();
        
        var content = Node.Document.Content.Substring(
            Node.RawNode.StartOffset, 
            Node.RawNode.EndOffset - Node.RawNode.StartOffset
        );
        
        sb.AppendLine(content);
        sb.AppendLine();
        
        return sb.ToString();
    }
}