using System.Text;
using System.Text.RegularExpressions;

namespace KnowledgeSystem.Plugins.Library.Tools.Workspace;

public sealed class ArtifactWorkspaceConfig
{
    public int MaxFileChars { get; set; } = 1_000_000;

    public int MaxFiles { get; set; } = 100;

    public int MaxTotalChars { get; set; } = 5_000_000;

    public int MaxGrepResults { get; set; } = 200;

    public int MaxReadLines { get; set; } = 500;

    public int MaxLineChars { get; set; } = 200;
}

public sealed record ArtifactFileInfo(string Name, int Size, int LineCount);

public sealed class ArtifactWorkspace(ArtifactWorkspaceConfig config)
{
    public ArtifactWorkspaceConfig Config { get; } = config;

    private static readonly Regex SafeNameRegex = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    private readonly object _lock = new();

    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    private int _totalChars;

    public bool TryWrite(string name, string content, out string? error)
    {
        if (!IsValidName(name))
        {
            error = $"Invalid file name '{name}'. Use letters, digits, '.', '_' and '-' only; no folders.";
            return false;
        }

        if (content.Length > Config.MaxFileChars)
        {
            error = $"Content too long ({content.Length} chars, limit {Config.MaxFileChars}).";
            return false;
        }

        lock (_lock)
        {
            if (_files.TryGetValue(name, out var existing))
            {
                _totalChars -= existing.Length;
            }
            else if (_files.Count >= Config.MaxFiles)
            {
                error = $"Workspace full ({Config.MaxFiles} files). Delete unused files with artifact_delete.";
                return false;
            }

            if (_totalChars + content.Length > Config.MaxTotalChars)
            {
                error = $"Workspace storage limit exceeded ({Config.MaxTotalChars} chars total). Delete unused files with artifact_delete.";
                return false;
            }

            _files[name] = content;
            _totalChars += content.Length;
        }

        error = null;
        return true;
    }

    public bool TryGet(string name, out string? content)
    {
        lock (_lock)
        {
            return _files.TryGetValue(name, out content);
        }
    }

    public bool TryEdit(string name, string find, string replace, out string? error, out int line)
    {
        line = 0;

        if (find.Length == 0)
        {
            error = "The find text must not be empty.";
            return false;
        }

        lock (_lock)
        {
            if (!_files.TryGetValue(name, out var content))
            {
                error = $"File '{name}' not found.";
                return false;
            }

            var matches = new List<int>();
            var index = 0;

            while (index < content.Length)
            {
                var at = content.IndexOf(find, index, StringComparison.Ordinal);

                if (at < 0)
                {
                    break;
                }

                matches.Add(at);
                index = at + find.Length;
            }

            if (matches.Count == 0)
            {
                error = BuildNotFoundHint(content, find);
                return false;
            }

            if (matches.Count > 1)
            {
                var lineNumbers = matches.Select(m => LineAt(content, m)).Distinct().ToList();
                error = $"The find text matches {matches.Count} times (lines {string.Join(", ", lineNumbers)}). Include more surrounding context in the find text to make it unique.";
                return false;
            }

            var at2 = matches[0];
            line = LineAt(content, at2);
            _totalChars += replace.Length - find.Length;
            _files[name] = content[..at2] + replace + content[(at2 + find.Length)..];
        }

        error = null;
        return true;
    }

    public bool TryDelete(string name, out string? error)
    {
        lock (_lock)
        {
            if (!_files.TryGetValue(name, out var content))
            {
                error = $"File '{name}' not found.";
                return false;
            }

            _files.Remove(name);
            _totalChars -= content.Length;
        }

        error = null;
        return true;
    }

    public IReadOnlyList<ArtifactFileInfo> List()
    {
        lock (_lock)
        {
            return _files
                .OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
                .Select(f => new ArtifactFileInfo(f.Key, f.Value.Length, SplitLines(f.Value).Length))
                .ToList();
        }
    }

    public bool TryGrep(string pattern, string? name, out string output, out string? error)
    {
        Regex regex;

        try
        {
            regex = new Regex(pattern, RegexOptions.Compiled);
        }
        catch (ArgumentException ex)
        {
            error = $"Invalid regex: {ex.Message}";
            output = string.Empty;
            return false;
        }

        var sb = new StringBuilder();
        var results = 0;

        lock (_lock)
        {
            foreach (var entry in _files.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (name != null && !entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var lines = SplitLines(entry.Value);

                for (var i = 0; i < lines.Length; i++)
                {
                    if (!regex.IsMatch(lines[i]))
                    {
                        continue;
                    }

                    var text = lines[i];

                    if (text.Length > Config.MaxLineChars)
                    {
                        text = text[..(Config.MaxLineChars - 1)] + "...";
                    }

                    sb.AppendLine($"{entry.Key}:{i + 1}: {text}");
                    results++;

                    if (results >= Config.MaxGrepResults)
                    {
                        output = sb.Append($"# truncated: more than {Config.MaxGrepResults} matches").ToString();
                        error = null;
                        return true;
                    }
                }
            }
        }

        if (results == 0)
        {
            error = "No matches found.";
            output = string.Empty;
            return false;
        }

        error = null;
        output = sb.ToString();
        return true;
    }

    public static bool IsValidName(string name)
    {
        return name.Length > 0 && SafeNameRegex.IsMatch(name);
    }

    public static string[] SplitLines(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');

        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        return lines;
    }

    private static string BuildNotFoundHint(string content, string find)
    {
        var firstLine = find
            .Replace("\r\n", "\n")
            .Split('\n')
            .FirstOrDefault(l => l.Trim().Length > 0);

        if (firstLine == null)
        {
            return "The find text was not found in the file.";
        }

        var trimmed = firstLine.Trim();
        var lines = SplitLines(content);
        var matches = new List<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(trimmed, StringComparison.Ordinal))
            {
                matches.Add(i + 1);
            }
        }

        return matches.Count > 0
            ? $"The find text was not found. Closest lines containing '{trimmed}': {string.Join(", ", matches.Take(10))}. Check exact whitespace and indentation."
            : "The find text was not found in the file. Check exact text and whitespace.";
    }

    private static int LineAt(string content, int index)
    {
        var line = 1;

        for (var i = 0; i < index; i++)
        {
            if (content[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
