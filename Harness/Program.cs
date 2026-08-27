using System.Diagnostics;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Events.Api;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.Library.Tools.WriteAttachment;
using KnowledgeSystem.Plugins.Wiki.CodeRag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

var root = Path.Combine(Path.GetTempPath(), "ks_coderag_" + Guid.NewGuid().ToString("N"));
var repoA = Path.Combine(root, "repoA");
var repoB = Path.Combine(root, "repoB");
Directory.CreateDirectory(Path.Combine(repoA, "src"));
Directory.CreateDirectory(Path.Combine(repoA, "bin"));
Directory.CreateDirectory(Path.Combine(repoA, ".git"));
Directory.CreateDirectory(Path.Combine(repoB, "nested"));

File.WriteAllText(Path.Combine(repoA, "src", "AuthService.cs"),
    "using System;\n" +
    "using Microsoft.Extensions.Logging;\n" +
    "public sealed class AuthService\n" +
    "{\n" +
    "    private readonly ILogger<AuthService> _logger;\n" +
    "    public void Run() => _logger.LogInformation(\"hello\");\n" +
    "}\n");
File.WriteAllText(Path.Combine(repoA, "src", "Util.cs"), "public static class Util { }\n");
File.WriteAllText(Path.Combine(repoA, "README.md"), "# repoA\n");
File.WriteAllText(Path.Combine(repoA, "bin", "junk.cs"), "should be excluded\n");
File.WriteAllText(Path.Combine(repoA, ".git", "config"), "[core]\n");
File.WriteAllText(Path.Combine(repoB, "index.ts"), "export const x = 1;\n");
File.WriteAllText(Path.Combine(repoB, "nested", "deep.ts"), "// ILogger usage here\n");

var fileSystem = new CodeReposFileSystem(root);

var failures = 0;

void Check(string name, bool condition, string? detail = null)
{
    if (condition)
    {
        Console.WriteLine($"PASS {name}");
    }
    else
    {
        Console.WriteLine($"FAIL {name} {detail}");
        failures++;
    }
}

var allFiles = fileSystem.EnumerateFiles("").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
Check("enumerate root excludes junk", allFiles.SequenceEqual(new[]
{
    "repoA/README.md",
    "repoA/src/AuthService.cs",
    "repoA/src/Util.cs",
    "repoB/index.ts",
    "repoB/nested/deep.ts"
}, StringComparer.OrdinalIgnoreCase), string.Join(",", allFiles));

var repoAFiles = fileSystem.EnumerateFiles("repoA").ToList();
Check("enumerate subdir", repoAFiles.Count == 3 && repoAFiles.All(f => f.StartsWith("repoA/")), string.Join(",", repoAFiles));

Check("enumerate missing dir", fileSystem.EnumerateFiles("doesNotExist").ToList().Count == 0);

Check("resolve valid path", fileSystem.TryResolvePath("repoA/src/AuthService.cs", out var resolved) && resolved == Path.Combine(root, "repoA", "src", "AuthService.cs"), resolved);
Check("reject escape", !fileSystem.TryResolvePath("../escape.cs", out _));
Check("reject rooted", !fileSystem.TryResolvePath(Path.GetTempPath() + "x.cs", out _));
Check("reject traversal", !fileSystem.TryResolvePath("repoA/../../../escape.cs", out _));

var findTool = new ToolBuilder("code_find_files")
    .WithDescription("d")
    .WithRequiredStringArgument("pattern", "p", out var patternArg)
    .Build();
var findHandler = new CodeFindFilesToolHandler(findTool, patternArg, fileSystem, new CodeFindFilesToolConfig());
var findResult = await RunHandler(findHandler, new Dictionary<ToolArgument, object?> { [patternArg] = ".*\\.cs$" });
Check("find cs files", findResult.IsSuccessful && findResult.Output.Contains("AuthService.cs") && findResult.Output.Contains("Util.cs") && !findResult.Output.Contains("junk.cs"), findResult.Output);

var findNoMatch = await RunHandler(findHandler, new Dictionary<ToolArgument, object?> { [patternArg] = "ZzZz" });
Check("find no match errors", !findNoMatch.IsSuccessful && findNoMatch.ErrorMessage!.Contains("No files"), findNoMatch.ErrorMessage);

var readTool = new ToolBuilder("code_read_file")
    .WithDescription("d")
    .WithRequiredStringArgument("path", "p", out var pathArg)
    .WithIntegerArgument("startLine", "s", out var startLineArg)
    .WithIntegerArgument("endLine", "e", out var endLineArg)
    .Build();
var readHandler = new CodeReadFileToolHandler(readTool, pathArg, startLineArg, endLineArg, fileSystem, new CodeReadFileToolConfig());
var readRange = await RunHandler(readHandler, new Dictionary<ToolArgument, object?> { [pathArg] = "repoA/src/AuthService.cs", [startLineArg] = 4, [endLineArg] = 5 });
Check("read line range", readRange.IsSuccessful && readRange.Output.Contains("  4: {") && readRange.Output.Contains("private readonly ILogger<AuthService>"), Detail(readRange));

var readAll = await RunHandler(readHandler, new Dictionary<ToolArgument, object?> { [pathArg] = "repoA/src/AuthService.cs" });
Check("read whole file", readAll.IsSuccessful && readAll.Output.Contains("public sealed class AuthService"), readAll.Output);

var readMissing = await RunHandler(readHandler, new Dictionary<ToolArgument, object?> { [pathArg] = "repoA/src/Nope.cs" });
Check("read missing file errors", !readMissing.IsSuccessful && readMissing.ErrorMessage!.Contains("not found"), readMissing.ErrorMessage);

var readEscape = await RunHandler(readHandler, new Dictionary<ToolArgument, object?> { [pathArg] = "../secret.cs" });
Check("read escape rejected", !readEscape.IsSuccessful, readEscape.ErrorMessage);

var grepTool = new ToolBuilder("code_grep")
    .WithDescription("d")
    .WithRequiredStringArgument("pattern", "p", out var grepPatternArg)
    .WithRequiredStringArgument("path", "p", out var grepPathArg)
    .WithStringArgument("pathFilter", "f", out var filterArg)
    .Build();
var grepHandler = new CodeGrepToolHandler(grepTool, grepPatternArg, grepPathArg, filterArg, fileSystem, new CodeGrepToolConfig());
var grepAll = await RunHandler(grepHandler, new Dictionary<ToolArgument, object?> { [grepPatternArg] = "ILogger", [grepPathArg] = "" });
Check("grep across root", grepAll.IsSuccessful && grepAll.Output.Contains("AuthService.cs") && grepAll.Output.Contains("deep.ts"), Detail(grepAll));

var grepFiltered = await RunHandler(grepHandler, new Dictionary<ToolArgument, object?> { [grepPatternArg] = "ILogger", [grepPathArg] = "", [filterArg] = ".*\\.ts$" });
Check("grep pathFilter", grepFiltered.IsSuccessful && grepFiltered.Output.Contains("deep.ts") && !grepFiltered.Output.Contains("AuthService.cs"), Detail(grepFiltered));

var grepNoMatch = await RunHandler(grepHandler, new Dictionary<ToolArgument, object?> { [grepPatternArg] = "ZzZzZz", [grepPathArg] = "repoA" });
Check("grep no match errors", !grepNoMatch.IsSuccessful && grepNoMatch.ErrorMessage!.Contains("No matches"), grepNoMatch.ErrorMessage);

Check("split lines handles CRLF", CodeReposFileSystem.SplitLines("a\r\nb\nc\r\n").Length == 3);
Check("binary content detected", CodeReposFileSystem.IsBinaryContent("ab\0cd"));
var rgTool = new ToolBuilder("code_ripgrep")
    .WithDescription("d")
    .WithRequiredArrayArgument("args", "a", out var argsArg)
    .Build();
var rgHandler = new CodeRipgrepToolHandler(rgTool, argsArg, fileSystem, new CodeRipgrepToolConfig());

Check("rg builder pattern positional", CodeRipgrepToolHandler.TryBuildArguments(new[] { "-i", "-n", "ILogger" }, fileSystem, out var rgBuilt1, out _)
    && rgBuilt1.SequenceEqual(new[] { "-i", "-n", "-e", "ILogger", "--", fileSystem.RootPath }), string.Join(",", rgBuilt1));
Check("rg builder explicit -e and path", CodeRipgrepToolHandler.TryBuildArguments(new[] { "-e", "ILogger", "repoA/src" }, fileSystem, out var rgBuilt2, out _)
    && rgBuilt2.SequenceEqual(new[] { "-e", "ILogger", "--", Path.Combine(root, "repoA", "src") }), string.Join(",", rgBuilt2));
Check("rg builder glob equals form", CodeRipgrepToolHandler.TryBuildArguments(new[] { "--glob=*.cs", "Foo" }, fileSystem, out var rgBuilt3, out _)
    && rgBuilt3[0] == "--glob=*.cs" && rgBuilt3.Contains("-e"), string.Join(",", rgBuilt3));
Check("rg builder files mode", CodeRipgrepToolHandler.TryBuildArguments(new[] { "--files", "repoA" }, fileSystem, out var rgBuilt4, out _)
    && rgBuilt4.SequenceEqual(new[] { "--files", "--", Path.Combine(root, "repoA") }), string.Join(",", rgBuilt4));
Check("rg builder rejects pre", !CodeRipgrepToolHandler.TryBuildArguments(new[] { "--pre", "cmd" }, fileSystem, out _, out _));
Check("rg builder rejects pattern file", !CodeRipgrepToolHandler.TryBuildArguments(new[] { "-f", "C:\\x" }, fileSystem, out _, out _));
Check("rg builder rejects pcre2", !CodeRipgrepToolHandler.TryBuildArguments(new[] { "--pcre2", "x" }, fileSystem, out _, out _));
Check("rg builder rejects combined shorts", !CodeRipgrepToolHandler.TryBuildArguments(new[] { "-in", "x" }, fileSystem, out _, out _));
Check("rg builder rejects stdin", !CodeRipgrepToolHandler.TryBuildArguments(new[] { "-" }, fileSystem, out _, out _));
Check("rg builder rejects bad int value", !CodeRipgrepToolHandler.TryBuildArguments(new[] { "-C", "-i", "x" }, fileSystem, out _, out _));
Check("rg builder rejects missing value", !CodeRipgrepToolHandler.TryBuildArguments(new[] { "-e" }, fileSystem, out _, out _));
Check("rg builder rejects escape path", !CodeRipgrepToolHandler.TryBuildArguments(new[] { "ILogger", ".." }, fileSystem, out _, out _));
Check("rg builder rejects rooted path", !CodeRipgrepToolHandler.TryBuildArguments(new[] { "ILogger", "C:\\Windows" }, fileSystem, out _, out _));

var rgMatch = await RunHandler(rgHandler, new Dictionary<ToolArgument, object?> { [argsArg] = new[] { "-i", "-n", "-e", "ILogger" } });
Check("rg end-to-end match", rgMatch.IsSuccessful && rgMatch.Output.Contains("AuthService.cs") && rgMatch.Output.Contains("deep.ts"), Detail(rgMatch));

var rgScoped = await RunHandler(rgHandler, new Dictionary<ToolArgument, object?> { [argsArg] = new[] { "-e", "ILogger", "repoB" } });
Check("rg end-to-end scoped path", rgScoped.IsSuccessful && rgScoped.Output.Contains("deep.ts") && !rgScoped.Output.Contains("AuthService.cs"), Detail(rgScoped));

var rgNoMatch = await RunHandler(rgHandler, new Dictionary<ToolArgument, object?> { [argsArg] = new[] { "-e", "ZzZzZz" } });
Check("rg end-to-end no match", !rgNoMatch.IsSuccessful && rgNoMatch.ErrorMessage!.Contains("No matches"), rgNoMatch.ErrorMessage);

var rgBlocked = await RunHandler(rgHandler, new Dictionary<ToolArgument, object?> { [argsArg] = new[] { "--pre", "notepad" } });
Check("rg end-to-end blocked flag", !rgBlocked.IsSuccessful && rgBlocked.ErrorMessage!.Contains("disallowed"), rgBlocked.ErrorMessage);

var rgContext = await RunHandler(rgHandler, new Dictionary<ToolArgument, object?> { [argsArg] = new[] { "-e", "ILogger", "-C", "1" } });
Check("rg end-to-end context", rgContext.IsSuccessful && rgContext.Output.Contains("public void Run"), Detail(rgContext));

var rgTruncatedHandler = new CodeRipgrepToolHandler(rgTool, argsArg, fileSystem, new CodeRipgrepToolConfig { MaxOutputChars = 100 });
var rgTruncated = await RunHandler(rgTruncatedHandler, new Dictionary<ToolArgument, object?> { [argsArg] = new[] { "-e", "." } });

var gitRoot = Path.Combine(root, "gitrepo");
Directory.CreateDirectory(gitRoot);
File.WriteAllText(Path.Combine(gitRoot, "readme.md"), "hello\n");

var gitAvailable = true;
try
{
    RunGit(gitRoot, "init");
    RunGit(gitRoot, "config", "user.name", "Harness");
    RunGit(gitRoot, "config", "user.email", "harness@local");
    RunGit(gitRoot, "add", "-A");
    RunGit(gitRoot, "commit", "-m", "initial");
    File.AppendAllText(Path.Combine(gitRoot, "readme.md"), "world\n");
    RunGit(gitRoot, "add", "-A");
    RunGit(gitRoot, "commit", "-m", "second");
}
catch (Exception)
{
    gitAvailable = false;
    Console.WriteLine("SKIP git end-to-end: git not found on PATH");
}

var gitTool = new ToolBuilder("code_git")
    .WithDescription("d")
    .WithRequiredArrayArgument("args", "a", out var gitArgsArg)
    .Build();
var gitHandler = new CodeGitToolHandler(gitTool, gitArgsArg, new CodeGitToolConfig { RepoPath = gitRoot });

Check("git builder requires subcommand", !CodeGitToolHandler.TryBuildArguments([], out _, out _));
Check("git builder rejects unknown subcommand", !CodeGitToolHandler.TryBuildArguments(new[] { "frobnicate" }, out _, out _));
Check("git builder rejects write command", !CodeGitToolHandler.TryBuildArguments(new[] { "commit", "-m", "x" }, out _, out _));
Check("git builder rejects unknown flag", !CodeGitToolHandler.TryBuildArguments(new[] { "log", "--frobnicate" }, out _, out _));
Check("git builder rejects repo flag", !CodeGitToolHandler.TryBuildArguments(new[] { "log", "-C", "C:\\x" }, out _, out _));
Check("git builder rejects stdin", !CodeGitToolHandler.TryBuildArguments(new[] { "diff", "-" }, out _, out _));
Check("git builder rejects missing value", !CodeGitToolHandler.TryBuildArguments(new[] { "log", "--since" }, out _, out _));
Check("git builder rejects bad int value", !CodeGitToolHandler.TryBuildArguments(new[] { "log", "--max-count", "abc" }, out _, out _));
Check("git builder rejects escape path", !CodeGitToolHandler.TryBuildArguments(new[] { "log", "--", ".." }, out _, out _));
Check("git builder numeric short", CodeGitToolHandler.TryBuildArguments(new[] { "log", "-5" }, out var gitBuilt, out _)
    && gitBuilt.SequenceEqual(new[] { "log", "--max-count", "5" }), string.Join(",", gitBuilt));
Check("git builder inline value", CodeGitToolHandler.TryBuildArguments(new[] { "log", "--since=2 days ago", "--stat" }, out var gitBuilt2, out _)
    && gitBuilt2.Contains("--since") && gitBuilt2.Contains("2 days ago") && gitBuilt2.Contains("--stat"), string.Join(",", gitBuilt2));

if (gitAvailable)
{
    var gitLog = await RunHandler(gitHandler, new Dictionary<ToolArgument, object?> { [gitArgsArg] = new[] { "log", "--oneline" } });
    Check("git end-to-end log", gitLog.IsSuccessful && gitLog.Output.Contains("second") && gitLog.Output.Contains("initial"), Detail(gitLog));

    var gitDiff = await RunHandler(gitHandler, new Dictionary<ToolArgument, object?> { [gitArgsArg] = new[] { "diff", "--stat", "HEAD~1", "HEAD" } });
    Check("git end-to-end diff", gitDiff.IsSuccessful && gitDiff.Output.Contains("readme.md"), Detail(gitDiff));

    var gitStatus = await RunHandler(gitHandler, new Dictionary<ToolArgument, object?> { [gitArgsArg] = new[] { "status", "--short" } });
    Check("git end-to-end status", gitStatus.IsSuccessful, Detail(gitStatus));

    var gitShow = await RunHandler(gitHandler, new Dictionary<ToolArgument, object?> { [gitArgsArg] = new[] { "show", "--name-status", "HEAD" } });
    Check("git end-to-end show", gitShow.IsSuccessful && gitShow.Output.Contains("readme.md"), Detail(gitShow));

    var gitRevParse = await RunHandler(gitHandler, new Dictionary<ToolArgument, object?> { [gitArgsArg] = new[] { "rev-parse", "--short", "HEAD" } });
    Check("git end-to-end rev-parse", gitRevParse.IsSuccessful && gitRevParse.Output.Trim().Length > 0, Detail(gitRevParse));

    var gitBadRev = await RunHandler(gitHandler, new Dictionary<ToolArgument, object?> { [gitArgsArg] = new[] { "log", "bogus-rev" } });
    Check("git process error surfaces stderr", !gitBadRev.IsSuccessful && gitBadRev.ErrorMessage!.Contains("bogus-rev"), gitBadRev.ErrorMessage);

    var gitTiny = new CodeGitToolHandler(gitTool, gitArgsArg, new CodeGitToolConfig { RepoPath = gitRoot, MaxOutputChars = 40 });
    var gitTrunc = await RunHandler(gitTiny, new Dictionary<ToolArgument, object?> { [gitArgsArg] = new[] { "log", "--stat", "--max-count", "1" } });
    Check("git truncation marks output", gitTrunc.IsSuccessful && gitTrunc.Output.Contains("truncated at 40"), gitTrunc.Output);

    var parallelCalls = new[]
    {
        RunHandler(gitHandler, new Dictionary<ToolArgument, object?> { [gitArgsArg] = new[] { "log", "--oneline" } }),
        RunHandler(gitHandler, new Dictionary<ToolArgument, object?> { [gitArgsArg] = new[] { "status", "--short" } }),
        RunHandler(gitHandler, new Dictionary<ToolArgument, object?> { [gitArgsArg] = new[] { "rev-parse", "--short", "HEAD" } })
    };
    var parallelResults = await Task.WhenAll(parallelCalls);
    Check("git parallel calls serialize cleanly", parallelResults.All(r => r.IsSuccessful), string.Join(" | ", parallelResults.Select(r => r.IsSuccessful ? "ok" : (r.ErrorMessage ?? "?"))));
}

var dnrTool = new ToolBuilder("code_dotnetrip")
    .WithDescription("d")
    .WithRequiredArrayArgument("args", "a", out var dnrArgsArg)
    .Build();
var dnrHandler = new CodeDotnetripToolHandler(dnrTool, dnrArgsArg, new CodeDotnetripToolConfig { DllsDir = Path.Combine(root, "code_dlls") });

Check("dnr builder requires command", !CodeDotnetripToolHandler.TryBuildArguments([], out _, out _, out _, out _));
Check("dnr builder rejects unknown command", !CodeDotnetripToolHandler.TryBuildArguments(new[] { "frobnicate" }, out _, out _, out _, out _));
Check("dnr builder rejects --dir", !CodeDotnetripToolHandler.TryBuildArguments(new[] { "assemblies", "--dir", "x" }, out _, out _, out _, out _));
Check("dnr builder rejects unknown flag", !CodeDotnetripToolHandler.TryBuildArguments(new[] { "assemblies", "--frobnicate" }, out _, out _, out _, out _));
Check("dnr builder head tail", CodeDotnetripToolHandler.TryBuildArguments(new[] { "grep", "Foo", "--max", "20", "--head", "10", "--tail", "5" }, out var dnrBuilt, out var dnrHead, out var dnrTail, out _)
    && dnrHead == 10 && dnrTail == 5 && dnrBuilt.SequenceEqual(new[] { "grep", "Foo", "--max", "20" }), string.Join(",", dnrBuilt));
Check("dnr builder lines range", CodeDotnetripToolHandler.TryBuildArguments(new[] { "read", "Foo.Bar", "--lines", "10-20" }, out _, out _, out _, out _));
Check("dnr builder rejects bad lines", !CodeDotnetripToolHandler.TryBuildArguments(new[] { "read", "Foo", "--lines", "abc" }, out _, out _, out _, out _));
Check("dnr head tail apply", CodeDotnetripToolHandler.ApplyHeadTail("a\nb\nc\n", 2, null) == "a\nb"
    && CodeDotnetripToolHandler.ApplyHeadTail("a\nb\nc\n", null, 1) == "c"
    && CodeDotnetripToolHandler.ApplyHeadTail("a\nb\nc\n", 2, 1) == "b"
    && CodeDotnetripToolHandler.ApplyHeadTail("a\nb\nc\n", null, null) == "a\nb\nc\n");

var sourceDll = Path.Combine(AppContext.BaseDirectory, "KnowledgeSystem.Ai.dll");
if (File.Exists(sourceDll))
{
    Directory.CreateDirectory(Path.Combine(root, "code_dlls"));
    File.Copy(sourceDll, Path.Combine(root, "code_dlls", "KnowledgeSystem.Ai.dll"), true);

    var dnrAssemblies = await RunHandler(dnrHandler, new Dictionary<ToolArgument, object?> { [dnrArgsArg] = new[] { "assemblies" } });
    Check("dnr end-to-end assemblies", dnrAssemblies.IsSuccessful && dnrAssemblies.Output.Contains("KnowledgeSystem.Ai.dll"), Detail(dnrAssemblies));

    var dnrHelp = await RunHandler(dnrHandler, new Dictionary<ToolArgument, object?> { [dnrArgsArg] = new[] { "help" } });
    Check("dnr end-to-end help", dnrHelp.IsSuccessful && dnrHelp.Output.Contains("Usage"), Detail(dnrHelp));

    var dnrFind = await RunHandler(dnrHandler, new Dictionary<ToolArgument, object?> { [dnrArgsArg] = new[] { "find", "Provider", "--max", "5" } });
    Check("dnr end-to-end find", dnrFind.IsSuccessful && dnrFind.Output.Contains("ProviderConfig"), Detail(dnrFind));

    var dnrHeaded = await RunHandler(dnrHandler, new Dictionary<ToolArgument, object?> { [dnrArgsArg] = new[] { "--head", "2", "find", "Provider", "--max", "10" } });
    Check("dnr end-to-end head", dnrHeaded.IsSuccessful && dnrHeaded.Output.Split('\n').Length == 2 && dnrHeaded.Output.Contains("ProviderConfig"), Detail(dnrHeaded));

    var dnrTailed = await RunHandler(dnrHandler, new Dictionary<ToolArgument, object?> { [dnrArgsArg] = new[] { "--tail", "1", "find", "Provider", "--max", "10" } });
    var services = new ServiceCollection().BuildServiceProvider();
var eventManager = new AgentEventManager(null, services);
var received = new List<AddAttachmentEvent>();
eventManager.AddReceiver(new AttachmentCollector(received));
var runner = new AgentRunner<BasicContext>(new StubChatClient(), new StubAgent("test"), null, new BasicContext(), eventManager, CancellationToken.None);

var waTool = new ToolBuilder("write_attachment")
    .WithDescription("d")
    .WithRequiredStringArgument("name", "n", out var waNameArg)
    .WithRequiredStringArgument("content", "c", out var waContentArg)
    .Build();
var waHandler = new WriteAttachmentToolHandler(waTool, waNameArg, waContentArg);

var waOk = await RunHandler(waHandler, new Dictionary<ToolArgument, object?> { [waNameArg] = "report.md", [waContentArg] = "hello" }, runner);
Check("write_attachment dispatches event", waOk.IsSuccessful && received.Count == 1 && received[0].Name == "report.md" && received[0].Content == "hello", Detail(waOk));

var waBadName = await RunHandler(waHandler, new Dictionary<ToolArgument, object?> { [waNameArg] = "../evil.md", [waContentArg] = "x" }, runner);
Check("write_attachment rejects bad name", !waBadName.IsSuccessful && received.Count == 1, waBadName.ErrorMessage);

var waOversized = await RunHandler(waHandler, new Dictionary<ToolArgument, object?> { [waNameArg] = "big.txt", [waContentArg] = new string('x', 70000) }, runner);
Check("write_attachment rejects oversized", !waOversized.IsSuccessful && received.Count == 1, waOversized.ErrorMessage);

try
{
    Directory.Delete(root, true);
}
catch
{
    // ignore cleanup failure
}
}
else
{
    Console.WriteLine("SKIP dotnetrip end-to-end: KnowledgeSystem.Ai.dll not found in harness bin");
}

try
{
    Directory.Delete(root, true);
}
catch
{
    // ignore cleanup failure
}

try
{
    Directory.Delete(root, true);
}
catch
{
    // ignore cleanup failure
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURES");
return;

static void RunGit(string workingDirectory, params string[] arguments)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = "git",
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)!;
    process.WaitForExit();
}

static async Task<ToolExecutionResult> RunHandler(ToolHandler<BasicContext>.Plain handler, Dictionary<ToolArgument, object?> arguments, AgentRunner<BasicContext>? runner = null)
{
    var args = new ArgumentExtractionResult
    {
        Status = ArgumentExtractionResult.ExtractionStatus.Success,
        Arguments = arguments,
        MissingArguments = []
    };

    return await handler.ExecuteAsync(runner!, args, CancellationToken.None);
}
static string Detail(ToolExecutionResult result) => result.IsSuccessful ? result.Output : result.ErrorMessage ?? result.FormatError();
sealed class StubAgent(string agentId) : Agent<BasicContext>(agentId)
{
}

sealed class StubChatClient : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("stub");

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

sealed class AttachmentCollector(List<AddAttachmentEvent> received) : IEventReceiver
{
    [SubscribeEvent]
    public ValueTask OnAddAttachmentAsync(AddAttachmentEvent @event, CancellationToken cancellationToken)
    {
        received.Add(@event);
        return ValueTask.CompletedTask;
    }
}
