using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.Retrieval;
using KnowledgeSystem.Retrieval.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    services.AddRagServices(context.Configuration);
});

var host = builder.Build();

var engine = host.Services.GetRequiredService<RagEngine>();
await engine.InitializeAsync();

Console.WriteLine("Ready for queries\n\n");
var ctx = host.Services.GetRequiredService<RagDbContext>();

while (true)
{
    var q = Console.ReadLine();

    if (!string.IsNullOrEmpty(q))
    {
        var results = await engine.SearchAsync(q, 10);
        
        Console.WriteLine($"Results ({results.Length}):");

        foreach (var vectorSearchResult in results)
        {
            Console.WriteLine($"  {vectorSearchResult.Index}/{vectorSearchResult.Score:P2}");

            var chunkRecord = await ctx.Chunks.FirstAsync(x => x.HnswId == vectorSearchResult.Index);
            var document = engine.Repo.Documents[EmdReferencePath.CreateFile(chunkRecord.DocumentPath)];
            var chunk = document.ChunksByHexHash[chunkRecord.HashHex];
            
            Console.WriteLine($"    {chunk.ChunkText}");
        }
    }
}

return;