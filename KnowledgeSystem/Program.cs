using System.ClientModel;
using System.Text;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.Retrieval;
using KnowledgeSystem.Retrieval.Data;
using Microsoft.EntityFrameworkCore;
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
var ctx = host.Services.GetRequiredService<RagDbContext>();

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
    )
};

var history = new List<ChatMessage>
{
   new SystemChatMessage(await File.ReadAllTextAsync("system_prompt.md"))
};

var excludedIndices = new HashSet<int>();

while (true)
{
    Console.Write($"({history.Sum(x => x.Content.Sum(c => c.Text.Length))} chars, {excludedIndices.Count} vecs) > ");
    var userQuery = Console.ReadLine();

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
                if (toolCall.FunctionName == "semantic_search")
                {
                    using var jsonDoc = JsonDocument.Parse(toolCall.FunctionArguments);
                    var query = jsonDoc.RootElement.GetProperty("query").GetString()!;
                    
                    Console.WriteLine($"[Tool] semantic_search(\"{query}\")");

                    var chunks = new List<EmdChunk>();

                    foreach (var s in query.Split('|'))
                    {
                        var engineResults = await engine.SearchAsync(s, 5, excludedIndices: excludedIndices);
                    
                        foreach (var vectorSearchResult in engineResults.OrderByDescending(x => x.Score))
                        {
                            if (excludedIndices.Add(vectorSearchResult.Index))
                            {
                                var chunkRecord = await ctx.Chunks.FirstAsync(x => x.HnswId == vectorSearchResult.Index);
                                var document = engine.Repo.Documents[EmdReferencePath.CreateFile(chunkRecord.DocumentPath)];
                                var chunk = document.ChunksByHexHash[chunkRecord.HashHex];
                                chunks.Add(chunk);
                            }
                        }
                    }
                    
                    var sb = new StringBuilder();
                    foreach (var chunk in chunks)
                    {
                        sb.AppendLine($"--- Source: {chunk.Node.Document.Path} ---");
                        sb.AppendLine(chunk.ChunkText);
                        sb.AppendLine();
                    }
                    
                    //Console.WriteLine("Semantic Search Response -----------------------");
                    //Console.WriteLine(sb);
                    //Console.WriteLine("------------------------------------------------\n\n");
                    history.Add(new ToolChatMessage(toolCall.Id, sb.ToString()));
                }
                else if (toolCall.FunctionName == "repo_fetch")
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
                }
            }
        }
        else
        {
            var content = completion.Value.Content[0].Text;
            Console.WriteLine($"\nAssistant: {content}\n");
            history.Add(new AssistantChatMessage(content));
            break;
        }
    }
}
