namespace KnowledgeSystem.EmdParser.MarkdownTree;

public static class MarkdownExtensions
{
    public static bool IsHeading(this MarkdownNode.Type type)
    {
        return type switch
        {
            MarkdownNode.Type.H1 or MarkdownNode.Type.H2 or MarkdownNode.Type.H3 or MarkdownNode.Type.H4 or MarkdownNode.Type.H5 or MarkdownNode.Type.H6 => true,
            _ => false
        };
    }
}