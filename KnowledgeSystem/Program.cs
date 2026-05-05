using KnowledgeSystem.EmdParser.MarkdownTree;

var file = File.ReadAllText(@"c:\users\alioth\desktop\WeaponCore State Machine & Sync Architecture v3.0.md");
var md = MarkdownTreeParser.Parse(file);

using var writer = new StreamWriter(@"c:\users\alioth\desktop\tree.txt");
PrintTree(writer, md, 0);
return;

void PrintTree(StreamWriter w, MarkdownNode node, int depth)
{
    w.WriteLine(
        $"{new string(' ', depth * 4)}{node.NodeType} " +
        $"span: {node.StartOffset}..{node.EndOffset}) " +
        $"text: \"{node.Text.Replace("\\", @"\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")}\" " +
        $"children: {node.Children.Count}"
    );
    
    foreach (var child in node.Children)
    {
        PrintTree(w, child, depth + 1);
    }
}