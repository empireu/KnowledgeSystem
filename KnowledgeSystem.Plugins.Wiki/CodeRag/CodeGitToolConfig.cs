// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace KnowledgeSystem.Plugins.Wiki.CodeRag;

public sealed class CodeGitToolConfig
{
    public string? RepoPath { get; set; }

    public int MaxOutputChars { get; set; } = 32768;
}
