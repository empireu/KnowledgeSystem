using System.Text.Json;
using KnowledgeSystem.Ai;
using KnowledgeSystem.Events.Implementation;
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

var eventManager = new DefaultEventManager(null, null);
var observer = new ConsoleExtractionObserver();
eventManager.AddReceiver(observer);

var pipeline = new AgenticExtractionPipeline(new AgenticExtractionPipelineDescription
{
    ChatClient = client,
    EventManager = eventManager,
    SystemPrompt =
        """
        You are an information extraction system. Your job is to extract ALL named entities and ALL claims made about them from the provided text. Be thorough and comprehensive — extract as much as possible.

        You have three tools:
        - record_entity: Records a named entity. Call this for every entity you find.
        - record_claim: Records a claim about entities. Call this for every claim you find.
        - finish_extraction: Call this when you are done extracting everything.

        IMPORTANT: You can and SHOULD call multiple tools in parallel in a single response. Extract ALL entities first (or in parallel with claims), then extract claims.

        Extraction rules for entities:
        - Provide a primary name for each entity. If the entity is referred to by multiple names or aliases, include them in the aliases array.
        - The type field is free-form (e.g. "person", "organization", "ship", "location", "technology", "event", "concept").
        - The evidence field MUST be an EXACT, character-for-character substring from the source text. No paraphrasing. No modifications. If you cannot find an exact quote, do not record the entity.

        Extraction rules for claims:
        - The subject MUST be the primary name of an entity you have already recorded with record_entity. If you haven't recorded the entity yet, record it first.
        - The predicate should capture the SEMANTIC relationship — not just "said" or "was". Use specific, descriptive predicates. Examples by modality:
          - fact: "is stationed at", "was built by", "carried", "served as", "was heading toward", "had been destroyed"
          - opinion: "considers", "believes", "resents", "admires", "distrusts", "is suspicious of"
          - speculation: "might be", "could have been", "seemed to be", "appeared to be"
          - negation: "was not", "did not", "refused to", "denied"
          - command: "ordered", "warned against", "demanded", "instructed", "told ... to"
          - question: "wondered whether", "asked if", "questioned"
          - joke: "joked about", "teased", "sarcastically suggested"
          - emotion: "was afraid of", "felt grief over", "was angry at", "was relieved by", "was horrified by", "loved", "missed"
          - desire: "wanted to", "longed for", "wished", "hoped for", "craved", "needed"
          - intention: "planned to", "intended to", "meant to", "decided to", "resolved to", "was going to"
          - sensation: "felt pain in", "was dizzy from", "was nauseated by", "was cold", "was exhausted", "had a headache from"
          - action: "crossed arms", "stood up", "sat down", "nodded to", "slammed", "reached for", "leaned toward"
          - obligation: "had to", "was required to", "was supposed to", "was duty-bound to"
          - permission: "was allowed to", "was permitted to", "could"
          - ability: "was capable of", "could", "was able to", "was skilled at", "was unable to"
          - state: "was in lockdown", "was unconscious", "was running hot", "was damaged", "was in orbit around", "was located at"
          - possession: "owned", "had", "possessed", "carried", "was equipped with"
          - identity: "is a", "is the", "was formerly", "is known as"
          - causation: "caused", "led to", "resulted in", "forced ... to", "triggered", "was the reason for"
          - attribution: "according to", "was reported by", "was claimed by", "stated by"
          - comparison: "was faster than", "was larger than", "was better than", "was unlike", "was similar to"
        - Use object_entity when the object is a named entity you have already recorded. Use object_literal for values, phrases, or descriptions.
        - Prefer object_literal over object_entity when in doubt. Only use object_entity for clear, distinct named entities.
        - Exactly one of object_entity and object_literal must be provided per claim.
        - The evidence field MUST be an EXACT, character-for-character substring from the source text. No paraphrasing. No modifications. If you cannot find an exact quote, do not record the claim.
        - The modality field must be one of the following:
          - fact: stated as true without qualification (e.g. "The Canterbury was a water hauler")
          - opinion: subjective judgment or evaluation (e.g. "Miller thought it was a bullshit case")
          - speculation: uncertain or hypothesized (e.g. "Maybe someone was running silent")
          - negation: explicitly denied or contradicted (e.g. "Holden didn't trust Dawes")
          - command: imperative or directive (e.g. "Don't go chasing conspiracies")
          - question: interrogative (e.g. "Where had she gone?")
          - joke: humorous, sarcastic, or non-literal (e.g. "Nice of you to join us")
          - emotion: affective or emotional state — fear, anger, grief, love, relief, horror, disgust, joy, loneliness, etc. (e.g. "Holden felt a chill of dread")
          - desire: wanting, wishing, hoping, longing, needing (e.g. "Miller wanted to find Julie")
          - intention: plan, aim, resolve, decision (e.g. "Dawes planned to unite the Belt")
          - sensation: physical sensation — pain, cold, heat, nausea, dizziness, exhaustion, hunger, etc. (e.g. "Holden's head was starting to ache")
          - action: meaningful physical action — body language, gesture, significant movement. Only use for actions that carry narrative meaning (e.g. "Miller crossed his arms" = defensive posture). Do NOT use for trivial actions like "he sat" or "he stood".
          - obligation: duty, requirement, necessity (e.g. "Miller had to report to Shaddid")
          - permission: allowed, permitted (e.g. "They were cleared to dock")
          - ability: capability, capacity, skill — or lack thereof (e.g. "The Canterbury could make the run in two weeks")
          - state: condition, attribute, or status of an entity — including location, physical state, operational status (e.g. "Ceres was in lockdown", "The ship was running hot")
          - possession: ownership, having, control, equipment (e.g. "Holden had his own cabin")
          - identity: definitional, "is a", classification, naming (e.g. "The Canterbury is a water hauler")
          - causation: one thing caused, led to, or resulted in another (e.g. "The distress call caused them to divert")
          - attribution: sourced to or claimed by someone (e.g. "According to Shaddid, the case was closed")
          - comparison: relative, comparative, superlative, contrast (e.g. "The Rocinante was faster than the Canterbury")
        - Extract claims that reveal character, plot, world-building, relationships, or atmosphere. In narrative text, even small details matter: a headache reveals physical stress, a glance reveals social dynamics, a ship's condition reveals danger. Extract liberally.
        - Use the action modality for meaningful physical actions and body language (e.g. "crossed arms" = defensiveness, "slammed the door" = anger). Do NOT use action for trivial mechanical movements ("he sat", "he stood") — skip those entirely.
        - Only extract what is explicitly stated or can be directly inferred from the provided text.
        - Do not fabricate entities or claims.

        Strategy:
        1. Read the text carefully.
        2. Call record_entity for ALL entities you find, in parallel.
        3. Call record_claim for ALL claims — emotions, desires, intentions, sensations, states, relationships, facts, opinions, and more. Be thorough. You can interleave entity and claim calls if you're confident.
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

    /*RawProcessedIngestionChunk result;
    try
    {
        result = await pipeline.IngestAsync(chunk);
    }
    catch (Exception e)
    {
        Console.WriteLine($"Failure => {e.Message}");
        continue;
    }*/
    var result = await pipeline.IngestAsync(chunk);

    
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

