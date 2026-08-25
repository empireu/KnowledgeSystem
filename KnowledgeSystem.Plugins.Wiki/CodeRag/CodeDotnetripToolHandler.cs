using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Wiki.CodeRag;

public sealed class CodeDotnetripToolHandler(
    AgentTool tool,
    ArrayArgument argsArgument,
    CodeDotnetripToolConfig config
) : ToolHandler<BasicContext>.Plain(tool)
{
    private enum ArgValueKind
    {
        None,
        Any,
        Int,
        Lines
    }

    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal)
    {
        "read", "method", "grep", "list", "find", "glob", "references", "hierarchy", "search", "callgraph", "assemblies", "warmup", "usages", "help"
    };

    private static readonly Dictionary<string, ArgValueKind> AllowedArgs = new(StringComparer.Ordinal)
    {
        ["-F"] = ArgValueKind.None,
        ["--json"] = ArgValueKind.None,
        ["--regex"] = ArgValueKind.None,
        ["--inherited"] = ArgValueKind.None,
        ["--methods"] = ArgValueKind.None,
        ["--fields"] = ArgValueKind.None,
        ["--ctors"] = ArgValueKind.None,
        ["--properties"] = ArgValueKind.None,
        ["--inner-types"] = ArgValueKind.None,
        ["--public"] = ArgValueKind.None,
        ["--protected"] = ArgValueKind.None,
        ["--private"] = ArgValueKind.None,
        ["--internal"] = ArgValueKind.None,
        ["--static"] = ArgValueKind.None,
        ["--no-static"] = ArgValueKind.None,
        ["--quiet-skips"] = ArgValueKind.None,
        ["--max"] = ArgValueKind.Int,
        ["--context"] = ArgValueKind.Int,
        ["--lines"] = ArgValueKind.Lines,
        ["--sig"] = ArgValueKind.Any,
        ["--class"] = ArgValueKind.Any,
        ["--filter"] = ArgValueKind.Any,
        ["--skip-assembly"] = ArgValueKind.Any,
        ["--skip-type"] = ArgValueKind.Any,
        ["--kind"] = ArgValueKind.Any
    };

    private static readonly Regex LinesRegex = new(@"^\d+-\d+$", RegexOptions.Compiled);

    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider, CodeDotnetripToolConfig config)
    {
        var ripgrepTool = new ToolBuilder("code_dotnetrip")
            .WithDescription("Runs dotnetrip over the DLLs in the code_dlls folder. The first argument must be a dotnetrip command; pass 'help' as the first argument to see usage and all commands. Only a whitelisted set of flags is accepted. Use '--head N' or '--tail N' to keep only the first or last N lines of the output.")
            .WithRequiredArrayArgument("args", "Array of dotnetrip arguments, e.g. ['grep', 'ILogger', '--class', 'AuthService', '--max', '20', '--head', '30'].", out var argsArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<CodeDotnetripToolHandler>(
            serviceProvider,
            ripgrepTool,
            argsArg,
            config
        );

        registry.RegisterTool(ripgrepTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tokens = argsArgument.GetValue(args);

        if (!TryBuildArguments(tokens, out var arguments, out var head, out var tail, out var error))
        {
            return Error($"code_dotnetrip: {error}");
        }

        if (config.DllsDir is not { Length: > 0 } dllsDir)
        {
            return Error("code_dotnetrip: No DLL directory configured. Set 'wiki:CodeDllsDir' to enable this tool.");
        }

        var dllsPath = Path.IsPathRooted(dllsDir)
            ? dllsDir
            : Path.GetFullPath(dllsDir);

        if (!Directory.Exists(dllsPath))
        {
            return Error($"code_dotnetrip: DLL directory '{dllsDir}' not found. Put the DLLs to decompile in that folder.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = config.DotnetripPath ?? "dotnetrip",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add(dllsPath);

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
            return Error("code_dotnetrip: Failed to start dotnetrip. Make sure 'dotnetrip' is on the PATH or next to the executable.");
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
                return Success($"{ApplyHeadTail(output.ToString(), head, tail)}\n... output truncated at {config.MaxOutputChars} characters.");
            }

            await process.WaitForExitAsync(timeoutCts.Token);
            var stderr = await stderrTask;
            var processed = ApplyHeadTail(output.ToString(), head, tail);

            if (process.ExitCode == 0)
            {
                if (processed.Length == 0 && !string.IsNullOrWhiteSpace(stderr))
                {
                    return Success(stderr.Trim());
                }

                return Success(processed);
            }

            return Error($"code_dotnetrip: dotnetrip failed with exit code {process.ExitCode}: {stderr.Trim()}");
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

            return Error($"code_dotnetrip: Search timed out after {config.TimeoutSeconds} seconds.");
        }
    }

    public static bool TryBuildArguments(IReadOnlyList<string> tokens, out string[] arguments, out int? head, out int? tail, out string error)
    {
        arguments = [];
        head = null;
        tail = null;
        error = "";

        if (tokens.Count == 0)
        {
            error = "the first argument must be a dotnetrip command (read, method, grep, list, find, glob, references, hierarchy, search, callgraph, assemblies, warmup, usages, help)";
            return false;
        }

        var result = new List<string>();
        var commandSeen = false;

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
                    error = "standalone '-' is not a valid argument";
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

                if (flag is "--head" or "--tail")
                {
                    if (inlineValue == null)
                    {
                        if (i + 1 >= tokens.Count)
                        {
                            error = $"missing value for '{flag}'";
                            return false;
                        }

                        inlineValue = tokens[++i];
                    }

                    if (!int.TryParse(inlineValue, out var lines) || lines < 0)
                    {
                        error = $"'{inlineValue}' is not a valid non-negative integer";
                        return false;
                    }

                    if (flag == "--head")
                    {
                        head = lines;
                    }
                    else
                    {
                        tail = lines;
                    }

                    continue;
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
            }
            else
            {
                if (!commandSeen)
                {
                    if (!Commands.Contains(token))
                    {
                        error = $"unknown dotnetrip command '{token}'";
                        return false;
                    }

                    commandSeen = true;
                }

                result.Add(token);
            }
        }

        arguments = result.ToArray();
        return true;
    }

    public static string ApplyHeadTail(string output, int? head, int? tail)
    {
        if (head == null && tail == null)
        {
            return output;
        }

        var lines = output.Split('\n').ToList();

        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (head.HasValue && lines.Count > head.Value)
        {
            lines = lines.Take(head.Value).ToList();
        }

        if (tail.HasValue && lines.Count > tail.Value)
        {
            lines = lines.Skip(lines.Count - tail.Value).ToList();
        }

        return string.Join('\n', lines);
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
            case ArgValueKind.Lines when !LinesRegex.IsMatch(value):
                error = $"'{value}' is not a valid line range (e.g. 10-20)";
                return false;
        }

        return true;
    }
}
