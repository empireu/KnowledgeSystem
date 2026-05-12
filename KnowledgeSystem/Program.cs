using KnowledgeSystem;
using KnowledgeSystem.Retrieval;
using KnowledgeSystem.Retrieval.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

Console.WriteLine("Ready for queries\n");
string query;
do
{
    Console.Write("\n> ");
    query = Console.ReadLine()!;
} while (string.IsNullOrWhiteSpace(query));

var test = ActivatorUtilities.CreateInstance<Test>(host.Services, new Test.Description
{
    Endpoint = "http://127.0.0.1:1234/v1",
    Credentials = "none",
    Model = "google/gemma-4-e4b",
    SystemPrompt = File.ReadAllText("system_prompt.md"),
    WarningMessage = "**IMPORTANT:** I have done too much this round. " +
                     "I should record the findings and leave memory notes so I can continue next round!"
}, query);

var result = await test.Execute();
Console.WriteLine("\n");
Console.WriteLine(result ? "Search finished successfully." : "Search did not finish successfully.");
