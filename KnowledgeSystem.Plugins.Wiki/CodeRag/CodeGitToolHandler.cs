using System.Globalization;
using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Wiki.CodeRag;

public sealed class CodeGitToolHandler(
    AgentTool tool,
    ArrayArgument argsArgument,
    CodeGitToolConfig config
) : ToolHandler<BasicContext>.Plain(tool)
{
    private enum ArgValueKind
    {
        None,
        Any,
        Int
    }

    private enum DiffFormat
    {
        None,
        Patch,
        Stat,
        NumStat,
        NameStatus,
        NameOnly
    }

    private static readonly Dictionary<string, Dictionary<string, ArgValueKind>> AllowedArgs = new(StringComparer.Ordinal)
    {
        ["log"] = new(StringComparer.Ordinal)
        {
            ["-n"] = ArgValueKind.Int,
            ["--author"] = ArgValueKind.Any,
            ["--after"] = ArgValueKind.Any,
            ["--before"] = ArgValueKind.Any,
            ["--diff-filter"] = ArgValueKind.Any,
            ["--first-parent"] = ArgValueKind.None,
            ["--max-count"] = ArgValueKind.Int,
            ["--name-only"] = ArgValueKind.None,
            ["--name-status"] = ArgValueKind.None,
            ["--no-merges"] = ArgValueKind.None,
            ["--no-renames"] = ArgValueKind.None,
            ["--numstat"] = ArgValueKind.None,
            ["--oneline"] = ArgValueKind.None,
            ["--reverse"] = ArgValueKind.None,
            ["--since"] = ArgValueKind.Any,
            ["--stat"] = ArgValueKind.None,
            ["--until"] = ArgValueKind.Any
        },
        ["diff"] = new(StringComparer.Ordinal)
        {
            ["-p"] = ArgValueKind.None,
            ["--cached"] = ArgValueKind.None,
            ["--diff-filter"] = ArgValueKind.Any,
            ["--name-only"] = ArgValueKind.None,
            ["--name-status"] = ArgValueKind.None,
            ["--no-renames"] = ArgValueKind.None,
            ["--numstat"] = ArgValueKind.None,
            ["--patch"] = ArgValueKind.None,
            ["--staged"] = ArgValueKind.None,
            ["--stat"] = ArgValueKind.None
        },
        ["show"] = new(StringComparer.Ordinal)
        {
            ["-p"] = ArgValueKind.None,
            ["--diff-filter"] = ArgValueKind.Any,
            ["--name-only"] = ArgValueKind.None,
            ["--name-status"] = ArgValueKind.None,
            ["--no-renames"] = ArgValueKind.None,
            ["--numstat"] = ArgValueKind.None,
            ["--patch"] = ArgValueKind.None,
            ["--stat"] = ArgValueKind.None
        },
        ["status"] = new(StringComparer.Ordinal)
        {
            ["--branch"] = ArgValueKind.None,
            ["--porcelain"] = ArgValueKind.None,
            ["--short"] = ArgValueKind.None
        },
        ["rev-parse"] = new(StringComparer.Ordinal)
        {
            ["--abbrev-ref"] = ArgValueKind.None,
            ["--short"] = ArgValueKind.None,
            ["--verify"] = ArgValueKind.None
        }
    };

    private static readonly HashSet<string> AllowedSubcommands = new(AllowedArgs.Keys, StringComparer.Ordinal);

    private static readonly SemaphoreSlim ExecutionGate = new(1, 1);

    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider, CodeGitToolConfig config)
    {
        var gitTool = new ToolBuilder("code_git")
            .WithDescription("Runs read-only queries over the configured repository's git history (in-process, no external git binary) and returns text output. The first argument must be one of the read-only subcommands: log, diff, show, status, rev-parse. Only a whitelisted set of flags is accepted. Date values accept ISO dates or 'N days ago' style. Use '--' before path arguments, which are relative to the repository root. Examples: ['log', '--oneline', '--since=2 days ago'], ['log', '--stat', '--since=1 day ago'], ['diff', '--stat', 'HEAD~1', 'HEAD'], ['show', '--name-status', 'HEAD'], ['status', '--short'], ['rev-parse', '--short', 'HEAD'].")
            .WithRequiredArrayArgument("args", "Array of git arguments. The first element is the read-only subcommand (log, diff, show, status, rev-parse); the rest are flags and arguments, e.g. ['log', '--oneline', '--since=2 days ago']. Paths after '--' are relative to the repository root.", out var argsArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<CodeGitToolHandler>(
            serviceProvider,
            gitTool,
            argsArg,
            config
        );

        registry.RegisterTool(gitTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (config.RepoPath is not { Length: > 0 } repoPath)
        {
            return Error("code_git: No repository path configured. Set 'wiki:CodeGitRepoPath' to enable this tool.");
        }

        if (!Directory.Exists(repoPath))
        {
            return Error($"code_git: Repository path '{repoPath}' not found.");
        }

        var tokens = argsArgument.GetValue(args);

        if (!TryBuildArguments(tokens, out var arguments, out var error))
        {
            return Error($"code_git: {error}");
        }

        // LibGit2Sharp repositories are not thread-safe; serialize queries.
        if (!await ExecutionGate.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
        {
            return Error("code_git: Another git query is still running. Retry this call in a moment.");
        }

        try
        {
            return await Task.Run(() => ExecuteQuery(repoPath, arguments), cancellationToken);
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    public static bool TryBuildArguments(IReadOnlyList<string> tokens, out string[] arguments, out string error)
    {
        arguments = [];
        error = "";

        if (tokens.Count == 0)
        {
            error = "at least one subcommand is required (log, diff, show, status, rev-parse)";
            return false;
        }

        var subcommand = tokens[0];

        if (!AllowedSubcommands.Contains(subcommand))
        {
            error = $"unknown or disallowed subcommand '{subcommand}'";
            return false;
        }

        var allowedArgs = AllowedArgs[subcommand];
        var result = new List<string> { subcommand };
        var afterSeparator = false;

        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Length == 0 || token.Contains('\0'))
            {
                error = "arguments must be non-empty and contain no null characters";
                return false;
            }

            if (afterSeparator)
            {
                if (token.Split('/', '\\').Any(segment => segment == ".."))
                {
                    error = $"path '{token}' escapes the repository";
                    return false;
                }

                result.Add(token);
                continue;
            }

            if (token == "--")
            {
                afterSeparator = true;
                result.Add(token);
                continue;
            }

            if (token == "-")
            {
                error = "reading from stdin is not allowed";
                return false;
            }

            if (token.StartsWith('-'))
            {
                if (subcommand == "log" && token.Length > 1 && token[1] is >= '0' and <= '9')
                {
                    // '-5' is git shorthand for '--max-count 5'.
                    result.Add("--max-count");
                    result.Add(token[1..]);
                    continue;
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

                if (!allowedArgs.TryGetValue(flag, out var kind))
                {
                    error = $"disallowed argument '{flag}' for '{subcommand}'";
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

                    result.Add(flag);
                    result.Add(inlineValue);
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
                result.Add(token);
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

        if (kind == ArgValueKind.Int && (!int.TryParse(value, out var intValue) || intValue < 0))
        {
            error = $"'{value}' is not a valid non-negative integer";
            return false;
        }

        return true;
    }

    private ToolExecutionResult ExecuteQuery(string repoPath, string[] arguments)
    {
        try
        {
            using var repo = new Repository(repoPath);
            var subcommand = arguments[0];
            var rest = arguments[1..];

            var output = subcommand switch
            {
                "log" => ExecuteLog(repo, rest),
                "diff" => ExecuteDiff(repo, rest),
                "show" => ExecuteShow(repo, rest),
                "status" => ExecuteStatus(repo, rest),
                _ => ExecuteRevParse(repo, rest)
            };

            return CapOutput(output);
        }
        catch (RepositoryNotFoundException)
        {
            return Error($"code_git: '{repoPath}' is not a git repository.");
        }
        catch (InvalidOperationException exception)
        {
            return Error($"code_git: {exception.Message}");
        }
        catch (LibGit2SharpException exception)
        {
            return Error($"code_git: git operation failed: {exception.Message}");
        }
    }

    private ToolExecutionResult CapOutput(string output)
    {
        if (output.Length <= config.MaxOutputChars)
        {
            return Success(output);
        }

        return Success(output[..config.MaxOutputChars] + $"\n... output truncated at {config.MaxOutputChars} characters.");
    }

    private static Dictionary<string, string> ParseOptions(string[] tokens, Dictionary<string, ArgValueKind> allowed, out List<string> revisions, out List<string> paths)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        revisions = [];
        paths = [];
        var afterSeparator = false;

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];

            if (afterSeparator)
            {
                paths.Add(token);
                continue;
            }

            if (token == "--")
            {
                afterSeparator = true;
                continue;
            }

            if (token.StartsWith('-'))
            {
                var kind = allowed[token];

                if (kind == ArgValueKind.None)
                {
                    options[token] = "";
                }
                else
                {
                    options[token] = tokens[++i];
                }
            }
            else
            {
                revisions.Add(token);
            }
        }

        return options;
    }

    private string ExecuteLog(Repository repo, string[] tokens)
    {
        var options = ParseOptions(tokens, AllowedArgs["log"], out var revisions, out var paths);

        if (revisions.Count > 1)
        {
            throw new InvalidOperationException("log accepts at most one revision");
        }

        var filter = new CommitFilter();

        if (revisions.Count == 1)
        {
            filter.IncludeReachableFrom = ResolveCommitOrThrow(repo, revisions[0]).Sha;
        }

        DateTimeOffset? sinceDate = null;
        if (options.TryGetValue("--since", out var since) || options.TryGetValue("--after", out since))
        {
            sinceDate = ParseGitDate(since);
        }

        DateTimeOffset? untilDate = null;
        if (options.TryGetValue("--until", out var until) || options.TryGetValue("--before", out until))
        {
            untilDate = ParseGitDate(until);
        }

        var authorPattern = options.GetValueOrDefault("--author");

        if (options.ContainsKey("--reverse"))
        {
            filter.SortBy = CommitSortStrategies.Time | CommitSortStrategies.Reverse;
        }

        if (options.ContainsKey("--first-parent"))
        {
            filter.FirstParentOnly = true;
        }

        var maxCount = options.TryGetValue("--max-count", out var maxCountValue)
            ? int.Parse(maxCountValue, CultureInfo.InvariantCulture)
            : int.MaxValue;
        var format = GetDiffFormat(options);
        var oneline = options.ContainsKey("--oneline");
        var noRenames = options.ContainsKey("--no-renames");
        var diffFilter = options.GetValueOrDefault("--diff-filter");
        var noMerges = options.ContainsKey("--no-merges");

        var builder = new StringBuilder();
        var count = 0;

        foreach (var commit in repo.Commits.QueryBy(filter))
        {
            if (noMerges && commit.Parents.Count() > 1)
            {
                continue;
            }

            if (sinceDate != null && commit.Author.When < sinceDate)
            {
                continue;
            }

            if (untilDate != null && commit.Author.When > untilDate)
            {
                continue;
            }

            if (authorPattern != null && !AuthorMatches(commit, authorPattern))
            {
                continue;
            }

            if (count >= maxCount)
            {
                break;
            }
            count++;

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            if (oneline)
            {
                builder.Append(ShortHash(commit)).Append(' ').AppendLine(FirstLine(commit.MessageShort));
            }
            else
            {
                AppendCommitHeader(builder, commit);
            }

            if (format != DiffFormat.Patch && format != DiffFormat.None)
            {
                var entries = GetCommitPatchEntries(repo, commit, noRenames);
                AppendDiff(builder, entries, format, diffFilter, paths);
            }
        }

        return builder.ToString();
    }

    private string ExecuteDiff(Repository repo, string[] tokens)
    {
        var options = ParseOptions(tokens, AllowedArgs["diff"], out var revisions, out var paths);
        var cached = options.ContainsKey("--cached") || options.ContainsKey("--staged");

        if (cached && revisions.Count > 0)
        {
            throw new InvalidOperationException("--cached is not allowed together with revisions");
        }

        var noRenames = options.ContainsKey("--no-renames");
        var format = GetDiffFormat(options);
        if (format == DiffFormat.None)
        {
            format = DiffFormat.Patch;
        }

        var entries = GetDiffPatchEntries(repo, revisions, cached, noRenames);
        var diffFilter = options.GetValueOrDefault("--diff-filter");
        var builder = new StringBuilder();
        AppendDiff(builder, entries, format, diffFilter, paths);
        return builder.ToString();
    }

    private string ExecuteShow(Repository repo, string[] tokens)
    {
        var options = ParseOptions(tokens, AllowedArgs["show"], out var revisions, out var paths);

        if (revisions.Count > 1)
        {
            throw new InvalidOperationException("show accepts at most one revision");
        }

        var commit = revisions.Count == 1 ? ResolveCommitOrThrow(repo, revisions[0]) : repo.Head.Tip;
        var noRenames = options.ContainsKey("--no-renames");
        var format = GetDiffFormat(options);
        if (format == DiffFormat.None)
        {
            format = DiffFormat.Patch;
        }

        var builder = new StringBuilder();
        AppendCommitHeader(builder, commit);
        builder.AppendLine();
        var entries = GetCommitPatchEntries(repo, commit, noRenames);
        AppendDiff(builder, entries, format, options.GetValueOrDefault("--diff-filter"), paths);
        return builder.ToString();
    }

    private string ExecuteStatus(Repository repo, string[] tokens)
    {
        var options = ParseOptions(tokens, AllowedArgs["status"], out _, out var paths);
        var builder = new StringBuilder();

        if (options.ContainsKey("--branch"))
        {
            builder.Append("## ").AppendLine(repo.Head.FriendlyName);
        }

        var statusOptions = new StatusOptions
        {
            DetectRenamesInIndex = true,
            DetectRenamesInWorkDir = true
        };

        if (paths.Count > 0)
        {
            statusOptions.PathSpec = paths.ToArray();
        }

        var status = repo.RetrieveStatus(statusOptions);

        foreach (var entry in status)
        {
            var indexChar = IndexStatusChar(entry.State);
            var workTreeChar = WorkTreeStatusChar(entry.State);
            var path = entry.FilePath.Replace('\\', '/');

            if (indexChar == 'R' || workTreeChar == 'R')
            {
                builder.Append('R').Append("  ").Append(path);
                var oldPath = (entry.HeadToIndexRenameDetails?.OldFilePath ?? entry.IndexToWorkDirRenameDetails?.OldFilePath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(oldPath) && oldPath != path)
                {
                    builder.Append(" -> ").Append(oldPath);
                }
                builder.AppendLine();
            }
            else
            {
                builder.Append(indexChar).Append(workTreeChar).Append(' ').AppendLine(path);
            }
        }

        return builder.ToString();
    }

    private string ExecuteRevParse(Repository repo, string[] tokens)
    {
        var options = ParseOptions(tokens, AllowedArgs["rev-parse"], out var revisions, out _);
        var rev = revisions.Count > 0 ? revisions[0] : "HEAD";

        if (options.ContainsKey("--verify"))
        {
            ResolveCommitOrThrow(repo, rev);
        }

        if (options.ContainsKey("--abbrev-ref"))
        {
            return repo.Head.FriendlyName;
        }

        if (options.ContainsKey("--short"))
        {
            return ShortHash(ResolveCommitOrThrow(repo, rev));
        }

        return ResolveCommitOrThrow(repo, rev).Sha;
    }

    private static IReadOnlyList<PatchEntryChanges> GetCommitPatchEntries(Repository repo, Commit commit, bool noRenames)
    {
        var compareOptions = BuildCompareOptions(noRenames);
        var parent = commit.Parents.FirstOrDefault();

        return parent == null
            ? repo.Diff.Compare<Patch>(null, commit.Tree, compareOptions).ToList()
            : repo.Diff.Compare<Patch>(parent.Tree, commit.Tree, compareOptions).ToList();
    }

    private static IReadOnlyList<PatchEntryChanges> GetDiffPatchEntries(Repository repo, List<string> revisions, bool cached, bool noRenames)
    {
        var compareOptions = BuildCompareOptions(noRenames);
        Patch patch;

        if (cached)
        {
            patch = repo.Diff.Compare<Patch>(repo.Head.Tip.Tree, DiffTargets.Index, null, null, compareOptions);
        }
        else if (revisions.Count == 0)
        {
            patch = repo.Diff.Compare<Patch>(null, false, null, compareOptions);
        }
        else if (revisions.Count == 1)
        {
            patch = repo.Diff.Compare<Patch>(ResolveCommitOrThrow(repo, revisions[0]).Tree, DiffTargets.WorkingDirectory, null, null, compareOptions);
        }
        else
        {
            var (oldRev, newRev) = SplitRange(revisions);
            patch = repo.Diff.Compare<Patch>(
                ResolveCommitOrThrow(repo, oldRev).Tree,
                ResolveCommitOrThrow(repo, newRev).Tree,
                compareOptions);
        }

        return patch.ToList();
    }

    private static (string Old, string New) SplitRange(List<string> revisions)
    {
        if (revisions.Count == 2)
        {
            return (revisions[0], revisions[1]);
        }

        var range = revisions[0];
        var separator = range.IndexOf("...", StringComparison.Ordinal);
        if (separator < 0)
        {
            separator = range.IndexOf("..", StringComparison.Ordinal);
        }

        if (separator < 0)
        {
            throw new InvalidOperationException($"invalid revision range '{range}'");
        }

        return (range[..separator], range[(separator + 2)..]);
    }

    private static LibGit2Sharp.CompareOptions BuildCompareOptions(bool noRenames)
    {
        return new LibGit2Sharp.CompareOptions
        {
            Similarity = noRenames ? SimilarityOptions.None : SimilarityOptions.Renames
        };
    }

    private static void AppendCommitHeader(StringBuilder builder, Commit commit)
    {
        builder.Append("commit ").AppendLine(commit.Sha);
        builder.Append("Author: ").Append(commit.Author.Name).Append(" <").Append(commit.Author.Email).AppendLine(">");
        builder.Append("Date:   ").AppendLine(commit.Author.When.ToString("ddd MMM d HH:mm:ss yyyy zzz", CultureInfo.InvariantCulture));
        builder.AppendLine();
        foreach (var line in commit.Message.TrimEnd('\n').Split('\n'))
        {
            builder.Append("    ").AppendLine(line);
        }
    }

    private static DiffFormat GetDiffFormat(Dictionary<string, string> options)
    {
        if (options.ContainsKey("--name-only"))
        {
            return DiffFormat.NameOnly;
        }

        if (options.ContainsKey("--name-status"))
        {
            return DiffFormat.NameStatus;
        }

        if (options.ContainsKey("--numstat"))
        {
            return DiffFormat.NumStat;
        }

        if (options.ContainsKey("--stat"))
        {
            return DiffFormat.Stat;
        }

        if (options.ContainsKey("--patch") || options.ContainsKey("-p"))
        {
            return DiffFormat.Patch;
        }

        return DiffFormat.None;
    }

    private static void AppendDiff(StringBuilder builder, IReadOnlyList<PatchEntryChanges> entries, DiffFormat format, string? diffFilter, List<string> paths)
    {
        var filtered = entries.Where(entry => MatchesPath(entry, paths)).ToList();

        if (!string.IsNullOrEmpty(diffFilter))
        {
            filtered = filtered.Where(entry => diffFilter.Contains(ChangeLetter(entry.Status))).ToList();
        }

        switch (format)
        {
            case DiffFormat.Patch:
                foreach (var entry in filtered)
                {
                    builder.AppendLine(entry.Patch.TrimEnd('\n'));
                }

                break;
            case DiffFormat.Stat:
                foreach (var entry in filtered)
                {
                    var additions = entry.LinesAdded;
                    var deletions = entry.LinesDeleted;
                    var marks = new string('+', additions) + new string('-', deletions);
                    builder.Append(' ').Append(entry.Path.Replace('\\', '/')).Append(" | ").Append(additions + deletions).Append(' ').AppendLine(marks);
                }

                builder.Append(' ').Append(filtered.Count).Append(" file(s) changed, ")
                    .Append(filtered.Sum(entry => entry.LinesAdded)).Append(" insertions(+), ")
                    .Append(filtered.Sum(entry => entry.LinesDeleted)).AppendLine(" deletions(-)");
                break;
            case DiffFormat.NumStat:
                foreach (var entry in filtered)
                {
                    builder.Append(entry.LinesAdded).Append('\t').Append(entry.LinesDeleted).Append('\t').AppendLine(entry.Path.Replace('\\', '/'));
                }

                break;
            case DiffFormat.NameStatus:
                foreach (var entry in filtered)
                {
                    builder.Append(ChangeLetter(entry.Status)).Append('\t').Append(entry.Path.Replace('\\', '/'));
                    var oldPath = entry.OldPath?.Replace('\\', '/');
                    if (entry.Status == ChangeKind.Renamed && !string.IsNullOrEmpty(oldPath) && oldPath != entry.Path.Replace('\\', '/'))
                    {
                        builder.Append('\t').Append(oldPath);
                    }

                    builder.AppendLine();
                }

                break;
            case DiffFormat.NameOnly:
                foreach (var entry in filtered)
                {
                    builder.AppendLine(entry.Path.Replace('\\', '/'));
                }

                break;
        }
    }

    private static bool MatchesPath(PatchEntryChanges entry, List<string> paths)
    {
        if (paths.Count == 0)
        {
            return true;
        }

        var path = entry.Path.Replace('\\', '/');

        return paths.Any(pattern =>
        {
            var normalized = pattern.Replace('\\', '/');
            return path.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(normalized.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static char ChangeLetter(ChangeKind kind)
    {
        return kind switch
        {
            ChangeKind.Added => 'A',
            ChangeKind.Deleted => 'D',
            ChangeKind.Modified => 'M',
            ChangeKind.Renamed => 'R',
            ChangeKind.Copied => 'C',
            ChangeKind.TypeChanged => 'T',
            _ => '?'
        };
    }

    private static char IndexStatusChar(FileStatus state)
    {
        if (state.HasFlag(FileStatus.NewInIndex))
        {
            return 'A';
        }

        if (state.HasFlag(FileStatus.ModifiedInIndex))
        {
            return 'M';
        }

        if (state.HasFlag(FileStatus.DeletedFromIndex))
        {
            return 'D';
        }

        if (state.HasFlag(FileStatus.RenamedInIndex))
        {
            return 'R';
        }

        if (state.HasFlag(FileStatus.TypeChangeInIndex))
        {
            return 'T';
        }

        return ' ';
    }

    private static char WorkTreeStatusChar(FileStatus state)
    {
        if (state.HasFlag(FileStatus.NewInWorkdir))
        {
            return 'A';
        }

        if (state.HasFlag(FileStatus.ModifiedInWorkdir))
        {
            return 'M';
        }

        if (state.HasFlag(FileStatus.DeletedFromWorkdir))
        {
            return 'D';
        }

        if (state.HasFlag(FileStatus.RenamedInWorkdir))
        {
            return 'R';
        }

        if (state.HasFlag(FileStatus.TypeChangeInWorkdir))
        {
            return 'T';
        }

        return ' ';
    }

    private static Commit ResolveCommitOrThrow(Repository repo, string rev)
    {
        try
        {
            return ResolveCommit(repo, rev);
        }
        catch (LibGit2SharpException exception)
        {
            throw new InvalidOperationException($"failed to resolve revision '{rev}': {exception.Message}");
        }
    }

    private static Commit ResolveCommit(Repository repo, string rev)
    {
        if (rev == "HEAD")
        {
            return repo.Head.Tip;
        }

        var tildeIndex = rev.IndexOf('~');
        if (tildeIndex > 0)
        {
            var baseRev = rev[..tildeIndex];
            var count = int.Parse(rev[(tildeIndex + 1)..], CultureInfo.InvariantCulture);
            var commit = ResolveCommit(repo, baseRev);
            for (var i = 0; i < count; i++)
            {
                commit = commit.Parents.FirstOrDefault() ?? throw new InvalidOperationException($"failed to resolve revision '{rev}': not enough parents");
            }

            return commit;
        }

        if (rev.StartsWith("HEAD^", StringComparison.Ordinal))
        {
            var count = rev.Length > 5 ? int.Parse(rev[5..], CultureInfo.InvariantCulture) : 1;
            var commit = repo.Head.Tip;
            for (var i = 0; i < count; i++)
            {
                commit = commit.Parents.FirstOrDefault() ?? throw new InvalidOperationException($"failed to resolve revision '{rev}': not enough parents");
            }

            return commit;
        }

        return repo.Lookup<Commit>(rev) ?? throw new InvalidOperationException($"failed to resolve revision '{rev}'");
    }

    private static DateTimeOffset ParseGitDate(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Equals("now", StringComparison.OrdinalIgnoreCase))
        {
            return DateTimeOffset.Now;
        }

        if (trimmed.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            return DateTimeOffset.Now.Date;
        }

        if (trimmed.Equals("yesterday", StringComparison.OrdinalIgnoreCase))
        {
            return DateTimeOffset.Now.Date.AddDays(-1);
        }

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 3 && parts[2].Equals("ago", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[0], out var amount) && amount >= 0)
        {
            var span = parts[1].TrimEnd('s') switch
            {
                "second" => TimeSpan.FromSeconds(amount),
                "minute" => TimeSpan.FromMinutes(amount),
                "hour" => TimeSpan.FromHours(amount),
                "day" => TimeSpan.FromDays(amount),
                "week" => TimeSpan.FromDays(7 * amount),
                "month" => TimeSpan.FromDays(30 * amount),
                "year" => TimeSpan.FromDays(365 * amount),
                _ => (TimeSpan?)null
            };

            if (span != null)
            {
                return DateTimeOffset.Now - span.Value;
            }
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"unsupported date '{value}' (use ISO dates or 'N days ago' style)");
    }

    private static bool AuthorMatches(Commit commit, string pattern)
    {
        var text = commit.Author.Name + " <" + commit.Author.Email + ">";
        return System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string ShortHash(Commit commit) => commit.Sha[..Math.Min(7, commit.Sha.Length)];

    private static string FirstLine(string text)
    {
        var index = text.IndexOf('\n');
        return index < 0 ? text : text[..index];
    }
}
