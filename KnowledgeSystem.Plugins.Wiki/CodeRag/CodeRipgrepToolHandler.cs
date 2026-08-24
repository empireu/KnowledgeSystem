using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Wiki.CodeRag;

public sealed class CodeRipgrepToolHandler(
    AgentTool tool,
    ArrayArgument argsArgument,
    CodeReposFileSystem fileSystem,
    CodeRipgrepToolConfig config
) : ToolHandler<BasicContext>.Plain(tool)
{
    private enum ArgValueKind
    {
        None,
        Any,
        Int,
        Size
    }

    private static readonly Dictionary<string, ArgValueKind> AllowedArgs = new(StringComparer.Ordinal)
    {
        ["-0"] = ArgValueKind.None,
        ["-a"] = ArgValueKind.None,
        ["-c"] = ArgValueKind.None,
        ["-F"] = ArgValueKind.None,
        ["-H"] = ArgValueKind.None,
        ["-I"] = ArgValueKind.None,
        ["-i"] = ArgValueKind.None,
        ["-L"] = ArgValueKind.None,
        ["-l"] = ArgValueKind.None,
        ["-n"] = ArgValueKind.None,
        ["-o"] = ArgValueKind.None,
        ["-q"] = ArgValueKind.None,
        ["-s"] = ArgValueKind.None,
        ["-S"] = ArgValueKind.None,
        ["-U"] = ArgValueKind.None,
        ["-u"] = ArgValueKind.None,
        ["-v"] = ArgValueKind.None,
        ["-w"] = ArgValueKind.None,
        ["-x"] = ArgValueKind.None,
        ["--case-sensitive"] = ArgValueKind.None,
        ["--count"] = ArgValueKind.None,
        ["--count-matches"] = ArgValueKind.None,
        ["--crlf"] = ArgValueKind.None,
        ["--files"] = ArgValueKind.None,
        ["--files-with-matches"] = ArgValueKind.None,
        ["--files-without-match"] = ArgValueKind.None,
        ["--fixed-strings"] = ArgValueKind.None,
        ["--follow"] = ArgValueKind.None,
        ["--hidden"] = ArgValueKind.None,
        ["--ignore-case"] = ArgValueKind.None,
        ["--invert-match"] = ArgValueKind.None,
        ["--json"] = ArgValueKind.None,
        ["--line-buffered"] = ArgValueKind.None,
        ["--line-number"] = ArgValueKind.None,
        ["--line-regexp"] = ArgValueKind.None,
        ["--max-columns-preview"] = ArgValueKind.None,
        ["--multiline"] = ArgValueKind.None,
        ["--multiline-dotall"] = ArgValueKind.None,
        ["--no-config"] = ArgValueKind.None,
        ["--no-filename"] = ArgValueKind.None,
        ["--no-heading"] = ArgValueKind.None,
        ["--no-ignore"] = ArgValueKind.None,
        ["--no-ignore-global"] = ArgValueKind.None,
        ["--no-ignore-parent"] = ArgValueKind.None,
        ["--no-ignore-vcs"] = ArgValueKind.None,
        ["--no-messages"] = ArgValueKind.None,
        ["--null"] = ArgValueKind.None,
        ["--only-matching"] = ArgValueKind.None,
        ["--passthru"] = ArgValueKind.None,
        ["--quiet"] = ArgValueKind.None,
        ["--smart-case"] = ArgValueKind.None,
        ["--stats"] = ArgValueKind.None,
        ["--text"] = ArgValueKind.None,
        ["--type-list"] = ArgValueKind.None,
        ["--vimgrep"] = ArgValueKind.None,
        ["--with-filename"] = ArgValueKind.None,
        ["--word-regexp"] = ArgValueKind.None,
        ["-A"] = ArgValueKind.Int,
        ["-B"] = ArgValueKind.Int,
        ["-C"] = ArgValueKind.Int,
        ["-d"] = ArgValueKind.Int,
        ["-j"] = ArgValueKind.Int,
        ["-m"] = ArgValueKind.Int,
        ["-M"] = ArgValueKind.Int,
        ["--after-context"] = ArgValueKind.Int,
        ["--before-context"] = ArgValueKind.Int,
        ["--context"] = ArgValueKind.Int,
        ["--max-columns"] = ArgValueKind.Int,
        ["--max-count"] = ArgValueKind.Int,
        ["--max-depth"] = ArgValueKind.Int,
        ["--threads"] = ArgValueKind.Int,
        ["--max-filesize"] = ArgValueKind.Size,
        ["-e"] = ArgValueKind.Any,
        ["-E"] = ArgValueKind.Any,
        ["-g"] = ArgValueKind.Any,
        ["-r"] = ArgValueKind.Any,
        ["-t"] = ArgValueKind.Any,
        ["-T"] = ArgValueKind.Any,
        ["--context-separator"] = ArgValueKind.Any,
        ["--encoding"] = ArgValueKind.Any,
        ["--glob"] = ArgValueKind.Any,
        ["--iglob"] = ArgValueKind.Any,
        ["--path-separator"] = ArgValueKind.Any,
        ["--regexp"] = ArgValueKind.Any,
        ["--replace"] = ArgValueKind.Any,
        ["--sort"] = ArgValueKind.Any,
        ["--sortr"] = ArgValueKind.Any,
        ["--type"] = ArgValueKind.Any,
        ["--type-add"] = ArgValueKind.Any,
        ["--type-not"] = ArgValueKind.Any
    };

    private static readonly Regex SizeRegex = new(@"^\d+(\.\d+)?[KMG]?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void Register(AgentToolRegistry<BasicContext> registry, CodeReposFileSystem fileSystem, IServiceProvider serviceProvider, CodeRipgrepToolConfig config)
    {
        var ripgrepTool = new ToolBuilder("code_ripgrep")
            .WithDescription("Runs ripgrep (rg) over the code repositories and returns its raw output. The search pattern is the first non-flag argument, or pass it explicitly with '-e'/'--regexp'; further non-flag arguments are treated as repo-relative paths to search (the whole code repositories root is searched when no path is given). Only a whitelisted set of ripgrep flags is allowed; pass flags as separate array elements ('-i', '-n', not '-in').")
            .WithRequiredArrayArgument("args", "Array of ripgrep arguments, e.g. ['-e', 'ILogger', '-C', '2', 'WeaponCore/src']. Paths are relative to the code repositories root.", out var argsArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<CodeRipgrepToolHandler>(
            serviceProvider,
            ripgrepTool,
            argsArg,
            fileSystem,
            config
        );

        registry.RegisterTool(ripgrepTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tokens = argsArgument.GetValue(args);

        if (!TryBuildArguments(tokens, fileSystem, out var arguments, out var error))
        {
            return Error($"code_ripgrep: {error}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = config.RipgrepPath ?? "rg",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = fileSystem.RootPath,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)!;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Error("code_ripgrep: Failed to start ripgrep. Make sure 'rg' is on the PATH or next to the executable.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));

        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        var buffer = new char[8192];
        var output = new StringBuilder();
        var truncated = false;

        try
        {
            while (output.Length < config.MaxOutputChars)
            {
                var read = await process.StandardOutput.ReadAsync(buffer, timeoutCts.Token);
                
                if (read == 0)
                {
                    break;
                }

                var remaining = config.MaxOutputChars - output.Length;
                if (read > remaining)
                {
                    output.Append(buffer, 0, remaining);
                    truncated = true;
                    break;
                }

                output.Append(buffer, 0, read);
            }

            if (truncated)
            {
                process.Kill(true);
                // ReSharper disable once MethodSupportsCancellation
                await process.WaitForExitAsync();
                await stderrTask;
                cancellationToken.ThrowIfCancellationRequested();
                return Success($"{output}\n... output truncated at {config.MaxOutputChars} characters.");
            }

            await process.WaitForExitAsync(timeoutCts.Token);
            var stderr = await stderrTask;

            if (process.ExitCode == 0)
            {
                return Success(output.ToString());
            }

            if (process.ExitCode == 1)
            {
                return Error("code_ripgrep: No matches found.");
            }

            return Error($"code_ripgrep: ripgrep failed with exit code {process.ExitCode}: {stderr.Trim()}");
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            try
            {
                // ReSharper disable once MethodSupportsCancellation
                await process.WaitForExitAsync();
                await stderrTask;
            }
            catch
            {
                // Ignored
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return Error($"code_ripgrep: Search timed out after {config.TimeoutSeconds} seconds.");
        }
    }

    public static bool TryBuildArguments(IReadOnlyList<string> tokens, CodeReposFileSystem fileSystem, out string[] arguments, out string error)
    {
        arguments = [];
        error = "";

        var result = new List<string>();
        var paths = new List<string>();
        string? pattern = null;
        var hasExplicitPattern = false;
        var filesOnly = tokens.Contains("--files");

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Length == 0 || token.Contains('\0'))
            {
                error = "arguments must be non-empty and contain no null characters";
                return false;
            }

            if (token.StartsWith('-'))
            {
                if (token == "-")
                {
                    error = "reading from stdin is not allowed";
                    return false;
                }

                var flag = token;
                string? inlineValue = null;

                if (token.StartsWith("--", StringComparison.Ordinal))
                {
                    var equalsIndex = token.IndexOf('=');
                    if (equalsIndex >= 0)
                    {
                        flag = token[..equalsIndex];
                        inlineValue = token[(equalsIndex + 1)..];
                    }
                }

                if (!AllowedArgs.TryGetValue(flag, out var kind))
                {
                    error = $"disallowed argument '{flag}'";
                    return false;
                }

                if (inlineValue != null)
                {
                    if (kind == ArgValueKind.None)
                    {
                        error = $"'{flag}' does not take a value";
                        return false;
                    }

                    if (!ValidateValue(kind, inlineValue, out error))
                    {
                        return false;
                    }

                    result.Add(token);
                }
                else if (kind == ArgValueKind.None)
                {
                    result.Add(token);
                }
                else
                {
                    if (i + 1 >= tokens.Count)
                    {
                        error = $"missing value for '{flag}'";
                        return false;
                    }

                    var value = tokens[++i];

                    if (!ValidateValue(kind, value, out error))
                    {
                        return false;
                    }

                    result.Add(flag);
                    result.Add(value);
                }

                if (flag is "-e" or "--regexp")
                {
                    hasExplicitPattern = true;
                }
            }
            else
            {
                if (pattern == null && !hasExplicitPattern && !filesOnly)
                {
                    pattern = token;
                    result.Add("-e");
                    result.Add(token);
                }
                else
                {
                    paths.Add(token);
                }
            }
        }

        result.Add("--");

        if (paths.Count == 0)
        {
            result.Add(fileSystem.RootPath);
        }
        else
        {
            foreach (var path in paths)
            {
                if (!fileSystem.TryResolvePath(path, out var fullPath))
                {
                    error = $"path '{path}' is outside the code repositories root";
                    return false;
                }

                result.Add(fullPath);
            }
        }

        arguments = result.ToArray();
        return true;
    }

    private static bool ValidateValue(ArgValueKind kind, string value, out string error)
    {
        error = "";

        if (value.Length == 0 || value.Contains('\0'))
        {
            error = "argument values must be non-empty and contain no null characters";
            return false;
        }

        switch (kind)
        {
            case ArgValueKind.Int when !int.TryParse(value, out var intValue) || intValue < 0:
                error = $"'{value}' is not a valid non-negative integer";
                return false;
            case ArgValueKind.Size when !SizeRegex.IsMatch(value):
                error = $"'{value}' is not a valid size (e.g. 500, 10K, 1G)";
                return false;
        }

        return true;
    }
}
