// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace KnowledgeSystem.Agent.Tools.FindFiles;

public sealed class FindFilesToolConfig
{
    /// <summary>
    ///     Maximum number of matching files to return.
    /// </summary>
    public int MaxResults { get; set; } = 30;
}
