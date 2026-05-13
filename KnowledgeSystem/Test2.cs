using System.ClientModel;
using System.Diagnostics;
using System.Text;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.Retrieval.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable LoopCanBeConvertedToQuery

namespace KnowledgeSystem;

public sealed class Test2
{
    private readonly ILogger<Test2> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly RagEngine _engine;
    private readonly string _query;
    private readonly ChatClient _client;

    #region Tools
    
    private readonly AgentTool _semanticSearchTool;
    private readonly StringArgument _queryArgument;

    private readonly AgentTool _repoFetchTool;
    private readonly StringArgument _referenceArgument;

    private readonly AgentTool _recordDiscoveryTool;
    private readonly StringArgument _discoveryArgument;
    private readonly StringArgument _memoryArgument;

    private readonly AgentTool _finishResearchTool;
    
    #endregion
    
    private readonly ToolSet _toolSet;
    
    private readonly string _systemPrompt, _warningMessage;
    
    public readonly AgentContext Context = new();
    public readonly List<string> Discoveries = [];
    public readonly StringBuilder DiscoveryString = new();
    public readonly List<string> Memories = [];
    public readonly HashSet<int> ExcludedIndices = [];

    public Test2(ILogger<Test2> logger, IServiceProvider serviceProvider, RagEngine engine, Description description, string query)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _engine = engine;
        _query = query;

        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(description.Endpoint),
        };
        
        var credentials = new ApiKeyCredential(description.Credentials);
        var client = new OpenAIClient(credentials, options);
        _client = client.GetChatClient(description.Model);
        
        _semanticSearchTool = new ToolBuilder("fast_context")
            .WithDescription("Searches the knowledge base for all information related to the topic. Provide a rich sentence to maximize recall!")
            .WithRequiredStringArgument("query", "The search query.", out _queryArgument)
            .Build();

        _repoFetchTool = new ToolBuilder("repo_fetch")
            .WithDescription("Fetches the content of a specific repository reference (file, definition, directory, or offsets).")
            .WithRequiredStringArgument("reference", "The EmdReferencePath string to fetch.", out _referenceArgument)
            .Build();

        _recordDiscoveryTool = new ToolBuilder("record_discovery")
            .WithDescription("Submits a discovery to the user. You must include the discovery itself, along with what to keep in your memory.")
            .WithRequiredStringArgument("discovery", "The fragment to submit. It must include ALL locations the data originates from!", out _discoveryArgument)
            .WithRequiredStringArgument("memory", "The information to keep in your own memory. It must include ALL locations and the search keywords, along with a SUMMARY of what you found!", out _memoryArgument)
            .Build();

        _finishResearchTool = new ToolBuilder("finish_research")
            .WithDescription("Completes the research. Only call when you are 100% done.")
            .Build();

        _toolSet = new ToolSet();
        _toolSet.AddTool(_semanticSearchTool);
        _toolSet.AddTool(_repoFetchTool);
        _toolSet.AddTool(_recordDiscoveryTool);
        _toolSet.AddTool(_finishResearchTool);

        _systemPrompt = description.SystemPrompt;
        _warningMessage = description.WarningMessage;
    }
    
    public async Task<bool> Execute()
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Beginning research for {q}", _query);
        Context.InsertSystem($"{_systemPrompt}\n# USER QUERY:\n{_query}");
        Context.InsertElement(new RoundStartMarker(1));
        
        while (true)
        {
            var chatOptions = new ChatCompletionOptions();
            _toolSet.AddToOptions(chatOptions);
            
            var result = await _client.CompleteChatAsync(Context.ChatMessages, chatOptions);

            if (result == null)
            {
                _logger.LogError("No response on completion");
                return false;
            }

            var completion = result.Value;
            
            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                Context.InsertAssistant(completion);

                foreach (var toolCall in completion.ToolCalls)
                {
                    if (!_toolSet.TryMatchTool(toolCall, out var tool))
                    {
                        _logger.LogError("Invalid tool {t}", toolCall.FunctionName);
                        Context.InsertChat(new ToolChatMessage(toolCall.FunctionName, "Invalid tool!"));
                        continue;
                    }
                    
                    var args = ToolSet.ExtractArguments(tool, toolCall.FunctionArguments);
                   
                    if (args.Status == ArgumentExtractionResult.ExtractionStatus.IncompleteArguments)
                    {
                        var missing = string.Join(", ", args.MissingArguments.Select(a => a.ArgumentName));
                        Context.InsertTool(tool, $"Error: missing required arguments: {missing}");
                        _logger.LogError("Missing arguments {a} for tool {t}", missing, tool.ToolId);
                        continue;
                    }
                    
                    if (tool == _semanticSearchTool)
                    {
                        await FastContext(args);
                    }
                    else if (tool == _repoFetchTool)
                    {
                        RepoFetch(args);
                    }
                    else if (tool == _recordDiscoveryTool)
                    {
                        RecordDiscovery(args);
                    }
                    else if (tool == _finishResearchTool)
                    {
                        var timeTaken = sw.Elapsed.TotalSeconds;
                        await File.WriteAllTextAsync("__research_result.md", DiscoveryString.ToString());

                        var sb = new StringBuilder();
                        var compressSb = new StringBuilder();
                        for (var elementIndex = 0; elementIndex < Context.Elements.Count; elementIndex++)
                        {
                            var timelineElement = Context.Elements[elementIndex];
                            
                            if (timelineElement is CompletedRoundMarker roundMarker)
                            {
                                for (var subElementIndex = 0;
                                     subElementIndex < roundMarker.Elements.Count;
                                     subElementIndex++)
                                {
                                    compressSb.AppendLine($"# Sub-Element {subElementIndex}:");
                                    compressSb.AppendLine(roundMarker.Elements[subElementIndex].ToLogFormat());
                                }

                                compressSb.Replace("\r\n", "\n");
                                compressSb.Replace("\r", "\n");
                                compressSb.Replace("\n", "\n    ");
                                sb.Append(compressSb);
                                compressSb.Clear();
                            }
                            else
                            {
                                sb.AppendLine($"# Element {elementIndex}:");
                                sb.AppendLine(timelineElement.ToLogFormat());
                            }
                        }

                        await File.WriteAllTextAsync("__research_history.md", sb.ToString());
                        _logger.LogInformation("Research complete. Results written to files. Time taken: {t} seconds", timeTaken);
                        return true;
                    }
                    else
                    {
                        throw new Exception($"Unimplemented tool {tool.ToolId}");
                    }
                }
            }
            else
            {
                _logger.LogError("Got LLM finish reason {r}", completion.FinishReason);
                Context.InsertAssistant("The system instructions specifically told me to only use tools. If I meant to end the research phase, I need to use the `finish_research()` tool.");
                continue;
            }
            
            var roundStartMarkerIndex = Context.MutableElements.FindLastIndex(x => x is RoundStartMarker);
            
            if (Context.MutableElements.Count - roundStartMarkerIndex > 5)
            {
                Context.InsertAssistant(_warningMessage);
                _logger.LogWarning("Warned for too much work in round");
            }
        }
    }

    #region Semantic Search
    
    private async Task FastContext(ArgumentExtractionResult args)
    { 
        var query = _queryArgument.GetValue(args);
        
        _logger.LogInformation("Tool: fast_context(\"{q}\")", query);

        var retrieval = ActivatorUtilities.CreateInstance<FastContextRetrieval>(_serviceProvider, new FastContextRetrieval.Description
        {
            Query = query
        });

        await retrieval.PrepareForRun();
        
        var step = 0;
        int chars;
        
        do
        {
            chars = retrieval.Step(10);
            step++;
        } while (!retrieval.IsExhausted);
        
        _logger.LogInformation(
            "Results: {s} steps, {d} docs, {t} trees, {r} refs, {c} chars", 
            step,
            retrieval.ReferencedDocuments.Count,
            retrieval.ReferencedDocuments.Values.Sum(x => x.BoundingTreesSorted.Count),
            retrieval.ReferencedDocuments.Values.Sum(x => x.References.Count),
            chars
        );

        var results = retrieval.ReferencedDocuments.Values.ToList();
        results.Sort((a, b) => a.AverageScore.CompareTo(b.AverageScore));
        
        var sb = new StringBuilder();
        sb.AppendLine($"# fast_context: {results.Count} documents found. Extracted {results.Sum(x => x.BoundingTreesSorted.Count)} sections:");
        foreach (var referencedDocument in results)
        {
            sb.AppendLine($"# Document: {referencedDocument.Document.Path} - {referencedDocument.BoundingTreesSorted.Count} sections");

            foreach (var boundingTree in referencedDocument.BoundingTreesSorted)
            {
                var start = boundingTree.Root.StartOffset;
                var length = boundingTree.Root.EndOffset - start;
                sb.AppendLine(referencedDocument.Document.Content.Substring(start, length));
                sb.AppendLine();
            }
        }
        
        //var sb = ResolveDependenciesAndOrderChunks(queryRootChunks, dependencyChunks);
        Context.InsertTool(_semanticSearchTool, sb.ToString());
        
        _logger.LogInformation("Injected {c} chars", sb.Length);
    }
    
    private static void FormatSemanticSearch(StringBuilder sb, List<EmdChunk> sources, List<EmdChunk> dependencies)
    {
        var sourceHashes = new HashSet<EmdChunkHash>(sources.Select(c => c.Hash));
        var dedupedDeps = dependencies.Where(d => !sourceHashes.Contains(d.Hash)).ToList();

        if (dedupedDeps.Count > 0)
        {
            sb.AppendLine("## Required Information");
            sb.AppendLine();

            foreach (var group in dedupedDeps.GroupBy(c => c.Node.Document.Path))
            {
                sb.AppendLine($"### {group.Key}");
                sb.AppendLine();
                foreach (var depChunk in group.OrderBy(x => x.Node.RawNode.StartOffset + x.StartOffset))
                {
                    var absStart = depChunk.Node.RawNode.StartOffset + depChunk.StartOffset;
                    var absEnd = absStart + depChunk.Length;
                    sb.AppendLine($"[Source: {depChunk.Node.Document.Path}:{absStart},{absEnd}]");
                    sb.AppendLine(depChunk.RawContent);
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine("## Search Results");
        sb.AppendLine();

        foreach (var chunk in sources)
        {
            var absStart = chunk.Node.RawNode.StartOffset + chunk.StartOffset;
            var absEnd = absStart + chunk.Length;
            sb.AppendLine($"[Source: {chunk.Node.Document.Path}:{absStart},{absEnd}]");
            sb.AppendLine(chunk.RawContent);
            sb.AppendLine();
        }
    }

    #endregion

    #region Repo Fetch

    private void RepoFetch(ArgumentExtractionResult args)
    {
        var argument = _referenceArgument.GetValue(args);

        if (!EmdReferencePath.TryParse(argument, out var refPath))
        {
            _logger.LogError("Tool: repo_fetch - malformed path \"{p}\"", argument);
            Context.InsertTool(_repoFetchTool,
                "Error: malformed call. Possible call formats:\n" +
                "    1. repo_fetch(\"path/to/directory\") - Displays the documents in a directory\n" +
                "    2. repo_fetch(\"path/to/file.md:offset1,offset2\") - Fetches the content from the file between the specified offsets.\n" +
                "    3. repo_fetch(\"path/to/file.md\") - Fetches the entire file\n"
            );
            
            return;
        }

        switch (refPath.Type)
        {
            case EmdReferencePath.ReferenceType.Directory:
            {
                RepoFetchDirectory(refPath, argument);
                return;
            }
            case EmdReferencePath.ReferenceType.File:
            {
                RepoFetchFile(refPath, argument);
                return;
            }
            case EmdReferencePath.ReferenceType.Definition:
            {
                RepoFetchDefinition(refPath, argument);
                return;
            }
            case EmdReferencePath.ReferenceType.Offsets:
            {
                RepoFetchOffsets(refPath, argument);
                return;
            }
            default:
                throw new Exception($"Unhandled ref type {refPath.Type}");
        }
    }

    private void RepoFetchDirectory(EmdReferencePath refPath, string argument)
    {
        var directory = refPath.RepositoryRelativePath;
        var documents = _engine.Repo.Documents
            .Where(x => x.Key.RepositoryRelativePath.StartsWith(directory, StringComparison.InvariantCulture))
            .Select(x => x.Value)
            .OrderBy(x => x.Content.Length)
            .ToList();

        if (documents.Count == 0)
        {
            _logger.LogError("Tool: repo_fetch(dir: \"{p}\") - no files", argument);
            Context.InsertTool(_repoFetchTool, $"Warning: directory \"{argument}\" not found!");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {documents.Count} documents in \"{argument}\":");

        foreach (var document in documents)
        {
            sb.AppendLine($"  - {document.Path} - {document.Content.Length} chars");
        }
                
        _logger.LogInformation("Tool: repo_fetch(dir: \"{p}\") - {c} documents", argument, documents.Count);
        Context.InsertTool(_repoFetchTool, sb.ToString());
    }
    
    private void RepoFetchFile(EmdReferencePath refPath, string argument)
    {
        var fileRef = refPath.GetFile();
        if (!_engine.Repo.Documents.TryGetValue(fileRef, out var document))
        {
            _logger.LogError("Tool: repo_fetch(file: \"{p}\") - document not found", argument);
            Context.InsertTool(_repoFetchTool, $"Error: document {fileRef.RepositoryRelativePath} not found!");
            return;
        }
        _logger.LogInformation("Tool: repo_fetch(file: \"{p}\") - {c} chars loaded", argument, document.Content.Length);
        Context.InsertTool(_repoFetchTool, $"Content of {argument}:\n{document.Content}");
    }
    
    private void RepoFetchDefinition(EmdReferencePath refPath, string argument)
    {
        var fileRef = refPath.GetFile();
        if (!_engine.Repo.Documents.TryGetValue(fileRef, out var document))
        {
            _logger.LogError("Tool: repo_fetch(def: \"{p}\") - document not found", argument);
            Context.InsertTool(_repoFetchTool, $"Error: document {fileRef.RepositoryRelativePath} not found!");
            return;
        }
                
        if (!document.NodesWithDefinition.TryGetValue(refPath, out var node))
        {
            _logger.LogError("Tool: repo_fetch(def: \"{p}\") - not found", argument);
            Context.InsertTool(_repoFetchTool, $"Error: definition {refPath.Definition} not found in file {fileRef.RepositoryRelativePath}!");
            return;
        }
                    
        var nodeContent = document.Content.Substring(node.RawNode.StartOffset, node.RawNode.EndOffset - node.RawNode.StartOffset);
        _logger.LogInformation("Tool: repo_fetch(section: \"{p}\") - {c} chars loaded", argument, nodeContent.Length);
        Context.InsertTool(_repoFetchTool, $"Content of {argument}:\n{nodeContent}");
    }
    
    private void RepoFetchOffsets(EmdReferencePath refPath, string argument)
    {
        var fileRef = refPath.GetFile();
        if (!_engine.Repo.Documents.TryGetValue(fileRef, out var document))
        {
            _logger.LogError("Tool: repo_fetch(offsets: \"{p}\") - document not found", argument);
            Context.InsertTool(_repoFetchTool, $"Error: document {fileRef.RepositoryRelativePath} not found!");
            return;
        }

        if (refPath.StartOffset < 0 || refPath.EndOffset <= 0 || refPath.EndOffset <= refPath.StartOffset)
        {
            _logger.LogError("Tool: repo_fetch(offsets: \"{p}\") - invalid", argument);
            Context.InsertTool(_repoFetchTool, $"Error: offsets {refPath.StartOffset},{refPath.EndOffset} are invalid! Offsets are zero-based. The first one is the start offset and is inclusive; the second one is the end offset and is exclusive.");
            return;
        }

        var end = refPath.EndOffset;
        var outOfBounds = false;

        if (end > document.Content.Length)
        {
            outOfBounds = true;
            end = document.Content.Length;
        }
                    
        var content = document.Content.Substring(refPath.StartOffset, refPath.EndOffset - refPath.StartOffset);

        if (outOfBounds)
        {
            _logger.LogInformation("Tool: repo_fetch(offsets: \"{p}\") - truncated to {end}; {c} chars loaded", argument, end, content.Length);
            Context.InsertTool(_repoFetchTool, 
                $"Warning: end offset was changed to {end} because it was out-of-bounds.\n" +
                $"Content of {argument}:\n{content}"
            );
        }
        else
        {
            _logger.LogInformation("Tool: repo_fetch(offsets: \"{p}\") - {c} chars loaded", argument, content.Length);
            Context.InsertTool(_repoFetchTool, $"Content of {argument}:\n{content}");
        }
    }

    #endregion

    #region Record Discovery

    private void RecordDiscovery(ArgumentExtractionResult args)
    {
        var discovery = _discoveryArgument.GetValue(args);
        var memory = _memoryArgument.GetValue(args);

        var elements = Context.MutableElements;
        var startMarkerIndex = elements.FindLastIndex(x => x is RoundStartMarker);
        var startMarker = (RoundStartMarker)elements[startMarkerIndex];
        var completeMarker = new CompletedRoundMarker(elements[(startMarkerIndex + 1)..]);
        
        elements[startMarkerIndex] = completeMarker;
        elements.RemoveRange(startMarkerIndex + 1, elements.Count - startMarkerIndex - 1);
        
        _logger.LogInformation("Tool: record_discovery(...{l1}, ...{l2}) finishes round {r}, compressing {e} elements",
            discovery.Length, 
            memory.Length,
            startMarker.Round,
            completeMarker.Elements.Count
        );
        
        Discoveries.Add(discovery);
        Memories.Add(memory);

        DiscoveryString.AppendLine($"# Round {startMarker.Round}:");
        DiscoveryString.AppendLine(discovery);
        DiscoveryString.AppendLine();

        var systemPrompt = ((ChatElement)Context.MutableElements[0]).Message;
        var adjustedSystemPrompt = new ChatElement(new SystemChatMessage($"{systemPrompt.Content[0].Text}\n# Round {startMarker.Round} memory:\n{memory}"));
        
        Context.MutableElements[0] = adjustedSystemPrompt;
        Context.InsertElement(new RoundStartMarker(startMarker.Round + 1));
    }

    #endregion
    
    public sealed class Description
    {
        public required string Endpoint { get; init; }
        public required string Credentials { get; init; }
        public required string Model { get; init; }
        public required string SystemPrompt { get; init; }
        public required string WarningMessage { get; init; }
    }

    public sealed class RoundStartMarker(int round) : IMarkerElement
    {
        public readonly int Round = round;
        
        public string ToLogFormat() => $"RoundStart({Round})";
    }

    public sealed class CompletedRoundMarker(List<ITimelineElement> elements) : IMarkerElement
    {
        public readonly List<ITimelineElement> Elements = elements;

        public string ToLogFormat()
        {
            var sb = new StringBuilder();
            
            foreach (var element in Elements)
            {
                sb.AppendLine(element.ToLogFormat());
            }

            return sb.ToString();
        }
    }
}