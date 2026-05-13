using System.Text;
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

var foo = ActivatorUtilities.CreateInstance<Foo>(host.Services, query);

var step = 0;
while (true)
{
    var charCount = await foo.Step(10);
    
    var documents = foo.ReferencedDocuments.Values
        .OrderBy(x => x.AverageScore)
        .ToList();
    
    Console.WriteLine($"Step {++step} - {charCount} chars:");
    for (var documentIndex = 0; documentIndex < documents.Count; documentIndex++)
    {
        var referencedDocument = documents[documentIndex];
        
        Console.WriteLine($"  {documentIndex}. {referencedDocument.Document.Path}: {referencedDocument.References.Count} refs, {referencedDocument.BoundingTreesSorted.Count} trees, {referencedDocument.AverageScore:F2} score");
        for (var treeIndex = 0; treeIndex < referencedDocument.BoundingTreesSorted.Count; treeIndex++)
        {
            var boundingTree = referencedDocument.BoundingTreesSorted[treeIndex];
            
            Console.WriteLine($"    {treeIndex}. {boundingTree.Root.NodeType} - \"{boundingTree.Root.Text}\" - {boundingTree.ReferenceCount} refs, {boundingTree.AverageScore:F2} score, {boundingTree.Root.EndOffset - boundingTree.Root.StartOffset} chars");
        }

        Console.WriteLine();
    }

    var min = documents
        .Min(document => document.References.Min(x => x.VectorResult.Score));
    
    var max = documents
        .Max(document => document.References.Max(x => x.VectorResult.Score));

    Console.Write($"\n\nMin: {min:F2}, max: {max:F2}");
    
    Console.ReadLine();
}

return;

var test = ActivatorUtilities.CreateInstance<Test>(host.Services, new Test.Description
{
    Endpoint = "http://127.0.0.1:1234/v1",
    //Endpoint = "https://openrouter.ai/api/v1",
    //Credentials = File.ReadAllText("key.txt"),
    Credentials = "none",
    Model = "google/gemma-4-e4b",
    SystemPrompt = File.ReadAllText("system_prompt.md"),
    WarningMessage = "**IMPORTANT:** I have done too much this round. " +
                     "I should record the findings and leave memory notes so I can continue next round!"
}, query);

var result = await test.Execute();
Console.WriteLine("\n");
Console.WriteLine(result ? "Search finished successfully." : "Search did not finish successfully.");
