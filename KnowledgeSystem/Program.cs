using System.ClientModel;
using KnowledgeSystem;
using KnowledgeSystem.Agent;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Context.TokenEstimation;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Retrieval;
using KnowledgeSystem.Retrieval.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
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

var agent = new SimpleChatAgent("chat", host.Services);
var context = new SimpleChatContext();
context.ChatContext.InsertSystem(File.ReadAllText("sp.md"));
var tokenizer = TokenizerHelper.Create(TokenizerInfo.Gemma("tokenizer/gemma-4"));

var observer = new Observer(tokenizer);

var key = File.Exists("key.txt") ? File.ReadAllText("key.txt").Trim() : "none";
var client = new OpenAIClient(
    new ApiKeyCredential(key),
    new OpenAIClientOptions
    {
        Endpoint = new Uri("http://127.0.0.1:1234/v1")
    } 
).GetChatClient("google/gemma4-e4b");

var compactor = new ContextCompactor(tokenizer, client, maxContextTokens: 14000);

Console.WriteLine("Ready\n");

while (true)
{
    Console.Write($"({tokenizer.CountTokens(context.ChatMessages)} tokens) > ");
    var input = Console.ReadLine();
    
    if (string.IsNullOrWhiteSpace(input))
    {
        break;
    }

    context.ChatContext.InsertUser(input);

    var runner = new AgentRunner<SimpleChatContext>(
        observer,
        client,
        agent,
        null,
        context,
        CancellationToken.None
    );

    while (true)
    {
        var turnResult = await runner.ExecuteTurn();
        
        Console.WriteLine($"Turn: {turnResult}");

        if (runner.IsFinished)
        {
            break;
        }
    }

    await compactor.CompactAsync(context.ChatContext);
}