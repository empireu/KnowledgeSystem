using System.Text.Json;
using KnowledgeSystem.Ai;
using KnowledgeSystem.Retrieval.Api.Graph;
using KnowledgeSystem.Retrieval.Graph;
using KnowledgeSystem.Retrieval.Graph.Extraction;

var providerConfig = new ProviderConfig
{
    Endpoint = "https://openrouter.ai/api/v1",
    Model = "xiaomi/mimo-v2.5",
    Key = (await File.ReadAllTextAsync("or_key.txt")).Trim(),
    ProviderType = ProviderType.Usual
};

var client = OpenAiChatClientFactory.Create(providerConfig);

var pipeline = new AgenticExtractionPipeline(new AgenticExtractionPipelineDescription
{
    ChatClient = client,
    SystemPrompt =
        """
        You are an information extraction system. Your job is to extract all named entities and the claims made about them from the provided text.

        You have three tools:
        - record_entity: Records a named entity. Call this for every entity you find.
        - record_claim: Records a claim about entities. Call this for every claim you find.
        - finish_extraction: Call this when you are done extracting everything.

        IMPORTANT: You can and SHOULD call multiple tools in parallel in a single response. Extract ALL entities first (or in parallel with claims), then extract claims.

        Extraction rules for entities:
        - Provide a primary name for each entity. If the entity is referred to by multiple names or aliases, include them in the aliases array.
        - The type field is free-form (e.g. "person", "organization", "ship", "location", "technology").
        - The evidence field MUST be an EXACT, character-for-character substring from the source text. No paraphrasing. No modifications. If you cannot find an exact quote, do not record the entity.

        Extraction rules for claims:
        - The subject MUST be the primary name of an entity you have already recorded with record_entity. If you haven't recorded the entity yet, record it first.
        - The predicate should capture the SEMANTIC relationship, not just "said". For example:
          - BAD: subject=Shaddid, predicate=said, object_literal="Don't go chasing conspiracies"
          - GOOD: subject=Shaddid, predicate=warned against, object_literal="chasing conspiracies", modality=command
          - BAD: subject=Miller, predicate=said, object_literal="It's a bullshit case"
          - GOOD: subject=Miller, predicate=considers, object_literal="a bullshit case", modality=opinion
        - Use object_entity when the object is a named entity you have already recorded. Use object_literal for values, phrases, or descriptions.
        - Prefer object_literal over object_entity when in doubt. Only use object_entity for clear, distinct named entities.
        - Exactly one of object_entity and object_literal must be provided per claim.
        - The evidence field MUST be an EXACT, character-for-character substring from the source text. No paraphrasing. No modifications. If you cannot find an exact quote, do not record the claim.
        - The modality field must be one of: fact, opinion, speculation, negation, command, question, joke.
          - fact: stated as true without qualification
          - opinion: subjective judgment
          - speculation: uncertain
          - negation: explicitly denied
          - command: imperative
          - question: interrogative
          - joke: humorous or non-literal
        - Only extract claims that convey MEANINGFUL information. Do NOT extract trivial physical actions (e.g. "closed his eyes", "took a sip of coffee", "nodded").
        - Only extract what is explicitly stated or can be directly inferred from the provided text.
        - Do not fabricate entities or claims.

        Strategy:
        1. Read the text carefully.
        2. Call record_entity for ALL entities you find, in parallel.
        3. Call record_claim for ALL meaningful claims, in parallel. You can interleave entity and claim calls if you're confident.
        4. Call finish_extraction when done.
        
        The provided text is:
        """
});

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

// Limit to 5 files for testing the agentic pipeline:
mdFiles = mdFiles.Take(5).ToArray();

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
    var chunk = new IngestionChunkSource(sourceText);

    RawProcessedIngestionChunk result;
    try
    {
        result = await pipeline.IngestAsync(chunk);
    }
    catch (Exception e)
    {
        Console.WriteLine($"Failure => {e.Message}");
        continue;
    }
    
    var cache = result.ToCache();
    var json = JsonSerializer.Serialize(cache, jsonOptions);
    await File.WriteAllTextAsync(cachePath, json);

    Console.WriteLine($"done. ({result.Entities.Length} entities, {result.Claims.Length} claims)");
}

var cacheFiles = sourceDir.GetFiles("*.extraction.json");

var allEntities = new List<(string SourceFile, CacheEntity Entity)>();
var allClaims = new List<(string SourceFile, CacheClaim Claim)>();

foreach (var cacheFile in cacheFiles)
{
    var json = await File.ReadAllTextAsync(cacheFile.FullName);
    var record = JsonSerializer.Deserialize<ExtractionCacheRecord>(json, jsonOptions);
    
    if (record == null)
    {
        continue;
    }

    var sourceFile = cacheFile.Name.Replace(".md.extraction.json", "");

    foreach (var entity in record.Entities)
    {
        allEntities.Add((sourceFile, entity));
    }

    foreach (var claim in record.Claims)
    {
        allClaims.Add((sourceFile, claim));
    }
}

Console.WriteLine($"\nLoaded {allEntities.Count} entities and {allClaims.Count} claims across {cacheFiles.Length} chapters.");

Console.WriteLine("  ENTITIES");

foreach (var (sourceFile, entity) in allEntities)
{
    var names = entity.Names is { Length: > 0 }
        ? $"{entity.Name} (aka {string.Join(", ", entity.Names)})"
        : entity.Name;

    Console.WriteLine($"  [{sourceFile}] {names}");
    Console.WriteLine($"    Type: {entity.Type ?? "unknown"}");
    Console.WriteLine($"    Description: {entity.Description ?? "(none)"}");
    Console.WriteLine($"    Evidence: {entity.Evidence?.QuotedText ?? "(none)"}");
    Console.WriteLine();
}

Console.WriteLine("  CLAIMS");

foreach (var (sourceFile, claim) in allClaims)
{
    var objectStr = claim.ObjectEntityName is not null
        ? $"→ [{claim.ObjectEntityName}]"
        : $"→ \"{claim.ObjectLiteral}\"";

    Console.WriteLine($"  [{sourceFile}] [{claim.SubjectName}] {claim.Predicate} {objectStr}");
    Console.WriteLine($"    Modality: {claim.Modality}");
    Console.WriteLine($"    Evidence: {claim.Evidence?.QuotedText ?? "(none)"}");
    Console.WriteLine();
}

Console.WriteLine("  SUMMARY");

var modalityCounts = allClaims
    .GroupBy(c => c.Claim.Modality)
    .OrderByDescending(g => g.Count());

Console.WriteLine($"  Total entities: {allEntities.Count}");
Console.WriteLine($"  Total claims:   {allClaims.Count}");
Console.WriteLine();
Console.WriteLine("  Claims by modality:");

foreach (var group in modalityCounts)
{
    var bar = new string('█', group.Count());
    Console.WriteLine($"    {group.Key,-14} {group.Count(),4}  {bar}");
}

var entityTypes = allEntities
    .GroupBy(e => e.Entity.Type ?? "(untyped)")
    .OrderByDescending(g => g.Count());

Console.WriteLine();
Console.WriteLine("  Entities by type:");

foreach (var group in entityTypes)
{
    var str = new string('▒', Math.Min(group.Count(), 40));
    Console.WriteLine($"    {group.Key,-20} {group.Count(),4}  {str}");
}

