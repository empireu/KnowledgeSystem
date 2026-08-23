namespace KnowledgeSystem.Plugins.Wiki.CodeRag;

public sealed class CodeReposFileSystem(string rootPath)
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "bin", "obj", "node_modules", "dist", "out", "__pycache__", ".venv"
    };

    public string RootPath { get; } = Path.GetFullPath(rootPath);

    public bool TryResolvePath(string relativePath, out string fullPath)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        try
        {
            var candidate = Path.GetFullPath(Path.Combine(RootPath, relativePath));
            var relative = Path.GetRelativePath(RootPath, candidate);

            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}"))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public IEnumerable<string> EnumerateFiles(string relativeDirectory)
    {
        var searchRoot = string.IsNullOrEmpty(relativeDirectory)
            ? RootPath
            : Path.Combine(RootPath, relativeDirectory);

        if (!Directory.Exists(searchRoot))
        {
            yield break;
        }

        foreach (var file in EnumerateFilesCore(searchRoot))
        {
            yield return file;
        }
    }

    private IEnumerable<string> EnumerateFilesCore(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            yield return Path.GetRelativePath(RootPath, file).Replace(Path.DirectorySeparatorChar, '/');
        }

        foreach (var subdirectory in Directory.EnumerateDirectories(directory))
        {
            if (ExcludedDirectories.Contains(Path.GetFileName(subdirectory)))
            {
                continue;
            }

            foreach (var file in EnumerateFilesCore(subdirectory))
            {
                yield return file;
            }
        }
    }

    public static string[] SplitLines(string content)
    {
        var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            return lines[..^1];
        }

        return lines;
    }

    public static bool IsBinaryContent(string content)
    {
        return content.IndexOf('\0') >= 0;
    }
}
