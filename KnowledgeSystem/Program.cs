using System.ClientModel;
using System.Text;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.Retrieval;
using KnowledgeSystem.Retrieval.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using OpenAI.Chat;
using Serilog;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddRagServices(context.Configuration);
    })
    .UseSerilog((context, loggerConfiguration) =>
    {
        loggerConfiguration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console();
    });

var host = builder.Build();

var engine = host.Services.GetRequiredService<RagEngine>();
await engine.InitializeAsync();

Console.WriteLine("Ready for queries\n\n");
var options = new OpenAIClientOptions
{
    Endpoint = new Uri("http://127.0.0.1:1234/v1"),
};

        
var credentials = new ApiKeyCredential("none");
var client = new OpenAIClient(credentials, options);
var chat = client.GetChatClient( "google/gemma-4-e4b");

var semanticSearchTool = new ToolBuilder("semantic_search")
    .WithDescription("Searches the knowledge base using a query and returns chunks of relevant information. NEVER returns the same results!")
    .WithRequiredStringArgument("query", "The search query. Use '|' to separate multiple queries.", out var queryArg)
    .WithIntegerArgument("maxCount", "The maximum number of results per query. Defaults to 10.", out var topResArg)
    .Build();

var repoFetchTool = new ToolBuilder("repo_fetch")
    .WithDescription("Fetches the content of a specific repository reference (file, definition, directory, or offsets).")
    .WithRequiredStringArgument("reference", "The EmdReferencePath string to fetch.", out var referenceArg)
    .Build();

var recordDiscoveryTool = new ToolBuilder("record_discovery")
    .WithDescription("Submits a discovery to the user. You must include the discovery itself, along with what to keep in your memory.")
    .WithRequiredStringArgument("discovery", "The fragment to submit. It must include ALL locations the data originates from!", out var discoveryArg)
    .WithRequiredStringArgument("memory", "The information to keep in your own memory. It must include ALL locations and the search keywords, along with a SUMMARY of what you found!", out var memoryArg)
    .Build();

var finishResearchTool = new ToolBuilder("finish_research")
    .WithDescription("Completes the research. Only call when you are 100% done.")
    .Build();

var toolSet = new ToolSet();
toolSet.AddTool(semanticSearchTool);
toolSet.AddTool(repoFetchTool);
toolSet.AddTool(recordDiscoveryTool);
toolSet.AddTool(finishResearchTool);

var tools = toolSet.Tools.Values.Select(t => t.Tool).ToList();

var systemPrompt = await File.ReadAllTextAsync("system_prompt.md");
var history = new List<ChatMessage>();

var discoveryRoundIndex = 1;

var excludedIndices = new HashSet<int>();
var discoveries = new StringBuilder();
var round = 0;
var fetchCallsThisRound = 0;

while (true)
{
    Console.Write($"({history.Sum(x => x.Content.Sum(c => c.Text.Length))} chars, {excludedIndices.Count} vecs) > ");
    var userQuery = Console.ReadLine();
    history.Add(new SystemChatMessage(systemPrompt + $"\nTHE USER QUERY IS: {userQuery}"));
    
    if (string.IsNullOrEmpty(userQuery))
    {
        continue;
    }
    
    while (true)
    {
        var chatOptions = new ChatCompletionOptions();
        foreach (var tool in tools)
        {
            chatOptions.Tools.Add(tool);
        }

        var completion = await chat.CompleteChatAsync(history, chatOptions);

        var initialFetches = fetchCallsThisRound;
        if (completion.Value.FinishReason == ChatFinishReason.ToolCalls)
        {
            history.Add(new AssistantChatMessage(completion.Value));

            foreach (var toolCall in completion.Value.ToolCalls)
            {
                if (!toolSet.TryMatchTool(toolCall, out var tool))
                {
                    throw new NotSupportedException($"Unsupported tool: {toolCall.FunctionName}");
                }

                var extraction = ToolSet.ExtractArguments(tool, toolCall.FunctionArguments);
                if (extraction.Status == ArgumentExtractionResult.ExtractionStatus.IncompleteArguments)
                {
                    var missing = string.Join(", ", extraction.MissingArguments.Select(a => a.ArgumentName));
                    history.Add(new ToolChatMessage(toolCall.Id, $"Error: missing required arguments: {missing}"));
                    continue;
                }
                
                if (tool == semanticSearchTool)
                {
                    ++fetchCallsThisRound;
                    var query = queryArg.GetValue(extraction);
                    var specified = false;
                    if (!topResArg.TryGetValue(extraction, out var topK))
                    {
                        topK = 25;
                    }
                    else
                    {
                        specified = true;
                    }

                    if (specified)
                    {
                        Console.WriteLine($"[Tool] semantic_search(\"{query}\", {topK})");
                    }
                    else
                    {
                        Console.WriteLine($"[Tool] semantic_search(\"{query}\")");
                    }

                    var dbQueryResultChunks = new List<EmdChunk>();

                    var chars = 0;
                    var engineResults = await engine.SearchAsync(query.Split('|'), topK, excludedIndices: excludedIndices);
                            
                    foreach (var vectorSearchResult in engineResults.SelectMany(x => x))
                    {
                        if (excludedIndices.Add(vectorSearchResult.Index))
                        {
                            var chunk = engine.GetChunkByHnswId(vectorSearchResult.Index);
                                
                            dbQueryResultChunks.Add(chunk);

                            chars += chunk.RawContent.Length;

                            if (chars > 10000)
                            {
                                break;
                            }
                        }
                    }
                        
                    var dependencyChunks = engine.ResolveDependencies(dbQueryResultChunks);

                    // Add dependency chunks to excluded set so they won't appear in future searches
                    foreach (var depChunk in dependencyChunks)
                    {
                        if (engine.TryGetHnswId(depChunk, out var depHnswId))
                        {
                            excludedIndices.Add(depHnswId);
                        }
                    }

                    var sb = ResolveDependenciesAndOrderChunks(dbQueryResultChunks, dependencyChunks);
                    history.Add(new ToolChatMessage(toolCall.Id, sb.ToString()));
                }
                else if (tool == repoFetchTool)
                {
                    ++fetchCallsThisRound;
                     var reference = referenceArg.GetValue(extraction);
                    
                        Console.WriteLine($"[Tool] repo_fetch(\"{reference}\")");
                    
                        string fetchResult;
                        try
                        {
                            var refPath = EmdReferencePath.Parse(reference);
                        
                            if (refPath.Type == EmdReferencePath.ReferenceType.Directory)
                            {
                                var sb = new StringBuilder();
                                sb.AppendLine($"Files in directory {refPath.RepositoryRelativePath}:");
                                foreach (var docKey in engine.Repo.Documents.Keys)
                                {
                                    if (docKey.RepositoryRelativePath.StartsWith(refPath.RepositoryRelativePath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        sb.AppendLine($"- {docKey.RepositoryRelativePath}");
                                    }
                                }
                                fetchResult = sb.ToString();
                            }
                            else
                            {
                                var fileRef = refPath.GetFile();
                                if (engine.Repo.Documents.TryGetValue(fileRef, out var doc))
                                {
                                    if (refPath.Type == EmdReferencePath.ReferenceType.File)
                                    {
                                        fetchResult = doc.Content;
                                    }
                                    else if (refPath.Type == EmdReferencePath.ReferenceType.Definition)
                                    {
                                        if (doc.NodesWithDefinition.TryGetValue(refPath, out var node))
                                        {
                                            fetchResult = doc.Content.Substring(node.RawNode.StartOffset, node.RawNode.EndOffset - node.RawNode.StartOffset);
                                        }
                                        else
                                        {
                                            fetchResult = $"Error: Definition {refPath.Definition} not found in {fileRef.RepositoryRelativePath}";
                                        }
                                    }
                                    else if (refPath.Type == EmdReferencePath.ReferenceType.Offsets)
                                    {
                                        fetchResult = doc.Content.Substring(refPath.StartOffset, refPath.EndOffset - refPath.StartOffset);
                                    }
                                    else
                                    {
                                        fetchResult = "Error: Unhandled reference type.";
                                    }
                                }
                                else
                                {
                                    fetchResult = $"Error: File {fileRef.RepositoryRelativePath} not found.";
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            fetchResult = $"Error processing reference: {ex.Message}";
                        }

                        history.Add(new ToolChatMessage(toolCall.Id, fetchResult));
                }
                else if (tool == recordDiscoveryTool)
                {
                    fetchCallsThisRound = 0;
                    var discovery = discoveryArg.GetValue(extraction);
                    var memory = memoryArg.GetValue(extraction);

                    discoveries.AppendLine($"# ROUND {round}:");
                    discoveries.Append(discovery);
                    discoveries.AppendLine();
                    
                    Console.WriteLine($"record_discovery(...{discovery.Length}, ...{memory.Length}) -> Discoveries now {discoveries.Length}");
                    
                    history.RemoveRange(discoveryRoundIndex, history.Count - discoveryRoundIndex);
                    history.Add(new AssistantChatMessage($"Research round {round++}\nMEMORY: \n  {memory}"));
                    discoveryRoundIndex = history.Count;
                }
                else if (tool == finishResearchTool)
                {
                    Console.WriteLine("\n--- RESEARCH COMPLETE ---\n");
                    Console.WriteLine(discoveries.ToString());
                    Console.WriteLine("\n\n");
                    await File.WriteAllTextAsync("__research_result.md", discoveries.ToString());
                    await File.WriteAllTextAsync("__research_history.md", string.Join("\n", history.SelectMany(x => x.Content.Select(x => x.Text))));
                    await File.WriteAllTextAsync("__research_discoveries.md", discoveries.ToString());
                    return;
                }
                else
                {
                    throw new Exception("Unimplemented tool handler");
                }
            }
            
            await File.WriteAllTextAsync("__research_history.md", string.Join("\n", history.SelectMany(x => x.Content.Select(x => x.Text))));
            await File.WriteAllTextAsync("__research_discoveries.md", discoveries.ToString());
        }
        else if (completion.Value.FinishReason == ChatFinishReason.Stop)
        {
            throw new Exception("Got LLM stop");
        }
        else
        {
            throw new Exception($"Unexpected finish reason: {completion.Value.FinishReason}");
        }

        if (fetchCallsThisRound > initialFetches)
        {
            if (fetchCallsThisRound > 5)
            {
                history.Add(new SystemChatMessage(
                    "WARNING! You have fetched too much information this round. " +
                    "Consider recording your results if you still have relevant data/leads, or concluding the research if you think the data has dried up."));
                Console.WriteLine($"WARN for {fetchCallsThisRound} fetches");
            }
            
            if (history.Sum(x => x.Content.Sum(x => x.Text.Length)) > 16000)
            {
                //history.Add(new SystemChatMessage("WARNING! You have too much information to sift through. Consider recording your results now, and including key points in the summary."));
                //Console.WriteLine($"WARN for too many chars");
            }
        }

        
    }
}

StringBuilder ResolveDependenciesAndOrderChunks(List<EmdChunk> sources, List<EmdChunk> dependencies)
{
    var sb = new StringBuilder();

    var sourceHashes = new HashSet<EmdChunkHash>(sources.Select(c => c.Hash));
    var dedupedDeps = dependencies.Where(d => !sourceHashes.Contains(d.Hash)).ToList();

    if (dedupedDeps.Count > 0)
    {
        sb.AppendLine("## Dependencies");
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

    return sb;
}