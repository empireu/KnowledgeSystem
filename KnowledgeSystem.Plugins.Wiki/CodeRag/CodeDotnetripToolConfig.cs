// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace KnowledgeSystem.Plugins.Wiki.CodeRag;

public sealed class CodeDotnetripToolConfig
{
    public string? DotnetripPath { get; set; }

    public string? DllsDir { get; set; }

    public int MaxOutputChars { get; set; } = 32768;

    public int TimeoutSeconds { get; set; } = 60;
}
