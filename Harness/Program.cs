using System.Text.Json;
using KnowledgeSystem.Ai;
using KnowledgeSystem.Retrieval.Api.Graph;
using KnowledgeSystem.Retrieval.Graph;

var providerConfig = new ProviderConfig
{
    Endpoint = "https://openrouter.ai/api/v1",
    Model = "xiaomi/mimo-v2.5",
    Key = (await File.ReadAllTextAsync("or_key.txt")).Trim(),
    ProviderType = ProviderType.Usual
};

var chatOptionsConfig = new ChatOptionsConfig
{
    ProviderOnly = "xiaomi/fp8"
};

var client = OpenAiChatClientFactory.Create(providerConfig);

var pipelineDescription = new BasicOneShotFeatureExtractionPipelineDescription
{
    ChatClient = client,
    StructuredCompletionFactory = chatOptionsConfig.CreateOptions
};

var pipeline = new BasicOneShotFeatureExtractionPipeline(pipelineDescription);

const string sourceFolder = "extraction_input";
var sourceDir = new DirectoryInfo(sourceFolder);

if (!sourceDir.Exists)
{
    sourceDir.Create();
    Console.WriteLine("We need the data");
    return;
}

var mdFiles = sourceDir.GetFiles("*.md");

if (mdFiles.Length == 0)
{
    Console.WriteLine($"No .md files found in '{sourceFolder}'.");
    return;
}

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
};

foreach (var mdFile in mdFiles)
{
    var cachePath = Path.Combine(sourceFolder, $"{mdFile.Name}.extraction.json");

    if (File.Exists(cachePath))
    {
        Console.WriteLine($"  [{mdFile.Name}] cache exists, skipping.");
        continue;
    }

    Console.Write($"  [{mdFile.Name}] extracting... ");
    var sourceText = await File.ReadAllTextAsync(mdFile.FullName);
    var chunk = new IngestionChunkSource(sourceText, []);

    RawProcessedIngestionChunk result;
    for (var i = 0;; i++)
    {
        if (i > 5)
        {
            throw new Exception("FUCK!");
        }
        
        try
        {

            result = await pipeline.IngestAsync(chunk);
            break;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failure {i} => {e.Message}");
        }
    }
    
    var cache = result.ToCache();
    var json = JsonSerializer.Serialize(cache, jsonOptions);
    await File.WriteAllTextAsync(cachePath, json);

    Console.WriteLine($"done. ({result.Entities.Length} entities, {result.Relationships.Length} rels, {result.Attributes.Length} attrs)");
}
