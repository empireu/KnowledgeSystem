namespace KnowledgeSystem.Plugins.Library.Tools.ListDir;

public sealed class ListDirToolConfig
{
    /// <summary>
    ///     Maximum number of entries (files plus subdirectories) to show in one listing.
    ///     If the directory contains more entries, the agent is told to narrow the path.
    /// </summary>
    public int MaxEntries { get; set; } = 50;
}
