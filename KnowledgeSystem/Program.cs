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
using Serilog.Events;

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
   //new SystemChatMessage("You are an expert internal documentation assistant. Your purpose is to answer user queries strictly using the provided internal Markdown documentation. \n\n**CORE DIRECTIVES:**\n1. NO EXTERNAL KNOWLEDGE: You must answer using ONLY the information returned by your tools. If the tools do not provide the answer, say: \"I do not have enough information to answer that.\"\n2. ALWAYS SEARCH FIRST: You must trigger `semantic_search` for every user query. Do not attempt to answer from memory.\n3. MULTI-SEARCH STRATEGY: For complex queries, execute multiple `semantic_search` calls (e.g., search for the whole concept, then search for individual keywords). Use full, descriptive sentences for your search queries to maximize vector DB retrieval.\n4. MANDATORY CITATIONS: Every claim in your final response must end with an inline citation using this exact format: (from: path/to/file.md@Section).\n\n**TOOL USE:**\nYou have access to two tools.\n\n1. `semantic_search`: Queries the vector database.\n2. `repo_fetch`: Pulls specific context. Acceptable path formats:\n   - \"path/to/file.md@Section\" (Pulls a specific section)\n   - \"path/to/file.md:10,20\" (Pulls text between zero-based index 10 and 20, exclusive)\n   - \"path/to/file.md\" (Pulls the entire file. Use rarely).\n**MANDATORY MULTI-TOPIC SEARCH PROTOCOL:**\nWhen the user's prompt contains multiple distinct concepts, entities, or questions, you must execute a \"Comprehensive Search Strategy\". You must execute MULTIPLE `semantic_search` calls to cover all bases.\n\n1. **Deconstruct the Prompt:** Identify all distinct topics.\n2. **Execute the Combinatorial Search:** \n   - **Step A (The Intersection):** Execute one search combining the terms to see if they relate to each other.\n   - **Step B (The Isolated Searches):** You MUST ALSO execute separate, isolated searches for each individual concept. This prevents common topics from drowning out rare topics in the database.\n   \n   **EXAMPLE: User asks about \"Topic A and Topic B\"**\n   - Call 1: `semantic_search(\"Relationship between Topic A and Topic B\")`\n   - Call 2: `semantic_search(\"Comprehensive overview of Topic A\")`\n   - Call 3: `semantic_search(\"Detailed information regarding Topic B\")`\n\n3. **Verify Completeness:** Review the chunks returned from all your searches. Did you get information on both topics? If one is still missing, try one more isolated search using synonyms for the missing topic before giving up.\n\n**DEPENDENCY HANDLING (CRITICAL):**\nIf `semantic_search` returns a chunk containing a ```[dependsOn: ...]``` tag, you MUST resolve this dependency before answering. \n- If the tag looks like ```[dependsOn: @Something]```, you resolve it by calling `repo_fetch` with the pattern: \"path/to/document.md@Something\" with \"path/to/document\" being the path from the `[Document: ...]` metadata from the chunk.\n- Otherwise, use repo_fetch with the indicated location.\n\n**RESPONSE FORMAT:**\nWhen constructing your final reply to the user, strictly adhere to this format:\nKeep your response concise. Quote directly when necessary. \nInclude citations immediately after the relevant fact, NOT just at the end of the response.\n\nExample Output:\nThe authentication module uses OAuth2 for standard logins (from: backend/auth.md@Overview). However, admin endpoints require a dedicated API key (from: backend/security.md@Admin-Endpoints).")
   new SystemChatMessage(await File.ReadAllTextAsync("system_prompt.md"))
};

while (true)
{
    Console.Write($"({history.Sum(x => x.Content.Sum(c => c.Text.Length))}) > ");
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
                        var engineResults = await engine.SearchAsync(s, 5);
                    
                        foreach (var vectorSearchResult in engineResults.OrderByDescending(x => x.Score))
                        {
                            var chunkRecord = await ctx.Chunks.FirstAsync(x => x.HnswId == vectorSearchResult.Index);
                            var document = engine.Repo.Documents[EmdReferencePath.CreateFile(chunkRecord.DocumentPath)];
                            var chunk = document.ChunksByHexHash[chunkRecord.HashHex];

                            if (!chunks.Contains(chunk))
                            {
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
