using System.ClientModel;
using System.Text;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using OpenAI.Chat;
using System.Text.Json;
using KnowledgeSystem.Retrieval.Engine;
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

var tools = new List<ChatTool>
{
    ChatTool.CreateFunctionTool(
        "semantic_search",
        "Searches the knowledge base using a query and returns 5 chunks of relevant information. NEVER returns the same results!",
        BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "The search query. Use '|' to separate multiple queries."
                }
            },
            "required": ["query"]
        }
        """)
    ),
    ChatTool.CreateFunctionTool(
        "repo_fetch",
        "Fetches the content of a specific repository reference (file, definition, directory, or offsets).",
        BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "reference": {
                    "type": "string",
                    "description": "The EmdReferencePath string to fetch."
                }
            },
            "required": ["reference"]
        }
        """)
    ),
    ChatTool.CreateFunctionTool(
        "record_discovery",
        "Submits a discovery to the user. You must include the discovery itself, along with what to keep in your memory.",
        BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "discovery": {
                    "type": "string",
                    "description": "The fragment to submit. It must include ALL locations the data originates from!"
                },
                "memory": {
                    "type": "string",
                    "description": "The information to keep in your own memory. It must include ALL locations and the search keywords, along with a SUMMARY of what you found!"
                }
            },
            "required": ["discovery", "memory"]
        }
        """)
    ),
    ChatTool.CreateFunctionTool(
        "finish_research",
        "Completes the research. Only call when you are 100% done.",
        BinaryData.FromString("""
        {
            "type": "object",
            "properties": {}
        }
        """)
    )
};

var systemPrompt = await File.ReadAllTextAsync("system_prompt.md");
var history = new List<ChatMessage>();

var discoveryRoundIndex = 1;

var excludedIndices = new HashSet<int>();
var discoveries = new StringBuilder();
var round = 0;

while (true)
{
    Console.Write($"({history.Sum(x => x.Content.Sum(c => c.Text.Length))} chars, {excludedIndices.Count} vecs) > ");
    var userQuery = Console.ReadLine();
    history.Add(new SystemChatMessage(systemPrompt + $"\nTHE USER QUERY IS: {userQuery}"));
    
    if (string.IsNullOrEmpty(userQuery))
    {
        continue;
    }

    history.Add(new UserChatMessage(userQuery));

    while (true)
    {
        var chatOptions = new ChatCompletionOptions();
        foreach (var tool in tools)
        {
            chatOptions.Tools.Add(tool);
        }

        var completion = await chat.CompleteChatAsync(history, chatOptions);

        if (completion.Value.FinishReason == ChatFinishReason.ToolCalls)
        {
            history.Add(new AssistantChatMessage(completion.Value));

            foreach (var toolCall in completion.Value.ToolCalls)
            {
                switch (toolCall.FunctionName)
                {
                    case "semantic_search":
                    {
                        using var jsonDoc = JsonDocument.Parse(toolCall.FunctionArguments);
                        var query = jsonDoc.RootElement.GetProperty("query").GetString()!;
                    
                        Console.WriteLine($"[Tool] semantic_search(\"{query}\")");

                        var dbQueryResultChunks = new List<EmdChunk>();
        
                        foreach (var s in query.Split('|'))
                        {
                            var engineResults = await engine.SearchAsync(s, 5, excludedIndices: excludedIndices);
                    
                            foreach (var vectorSearchResult in engineResults.OrderByDescending(x => x.Score))
                            {
                                if (excludedIndices.Add(vectorSearchResult.Index))
                                {
                                    dbQueryResultChunks.Add(engine.GetChunkByHnswId(vectorSearchResult.Index));
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
                        break;
                    }
                    case "repo_fetch":
                    {
                        using var jsonDoc = JsonDocument.Parse(toolCall.FunctionArguments);
                        var reference = jsonDoc.RootElement.GetProperty("reference").GetString()!;
                    
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
                        break;
                    }
                    case "record_discovery":
                    {
                        using var jsonDoc = JsonDocument.Parse(toolCall.FunctionArguments);
                        var discovery = jsonDoc.RootElement.GetProperty("discovery").GetString()!;
                        var memory = jsonDoc.RootElement.GetProperty("memory").GetString()!;

                        discoveries.AppendLine($"# ROUND {round}:");
                        discoveries.Append(discovery);
                        discoveries.AppendLine();
                    
                        Console.WriteLine($"record_discovery(...{discovery.Length}, ...{memory.Length}) -> Discoveries now {discoveries.Length}");
                    
                        history.RemoveRange(discoveryRoundIndex, history.Count - discoveryRoundIndex);
                        history.Add(new SystemChatMessage($"Research round {round++}\nMEMORY: \n  {memory}"));
                        discoveryRoundIndex = history.Count;
                        break;
                    }
                    case "finish_research":
                        Console.WriteLine("\n--- RESEARCH COMPLETE ---\n");
                        Console.WriteLine(discoveries.ToString());
                        Console.WriteLine("\n\n");
                        await File.WriteAllTextAsync("__research_result.md", discoveries.ToString());
                        return;
                    default:
                        throw new NotSupportedException($"Unsupported tool: {toolCall.FunctionName}");
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