using KnowledgeSystem.Ai;
using KnowledgeSystem.Events.Implementation;
using KnowledgeSystem.Plugins.Surveillance.Extraction;
using KnowledgeSystem.Plugins.Surveillance.Messages;
using KnowledgeSystem.Retrieval.Api.Graph;
using KnowledgeSystem.Retrieval.Graph.Extraction;

var archive = DiscordArchiveParser.Parse("D:\\Scrape\\DISCORD\\pit.json");
var c = new MessageChunker();

var chunks = c.Chunk(archive);

/*
for (var index = 0; index < chunks.Count; index++)
{
    var chunk = chunks[index];
    Console.WriteLine($"{index}: {chunk.SourceContent.Length}ch, {(chunk.EndedAt - chunk.StartedAt).TotalHours:F}h");
    Console.WriteLine(chunk.SourceContent.Replace("\n", "\n  "));
    Console.WriteLine("\n\n");
}
*/

var target = chunks[1433];

Console.WriteLine($"\n\nTARGET:\n{target.SourceContent}");

var seenUsers = new HashSet<ulong>();
var users = archive
    .Where(m => seenUsers.Add(m.User.UserId))
    .Select(m => m.User)
    .ToList();

var userListPrompt = string.Join("\n", users.Select(u =>
    $"- Nickname \"{u.Nickname}\" (username: {u.Username}, id: {u.UserId})"));

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

        """
        + $"""
Known users in this conversation. When registering entities, use the nickname as the primary name and include the username and any other names from the messages as aliases:

{userListPrompt}

The provided text is:
"""
});

Console.WriteLine("Running extraction on target chunk...");

var source = new IngestionChunkSource(target.SourceContent);
var result = await pipeline.IngestAsync(source);

Console.WriteLine($"Entities: {result.Entities.Length}");
foreach (var entity in result.Entities)
{
    var names = entity.DefinedNames.Length > 1
        ? $"{entity.DefinedNames[0]} (aka {string.Join(", ", entity.DefinedNames[1..])})"
        : entity.DefinedNames[0];

    Console.WriteLine($"  {names}");
    Console.WriteLine($"    Type: {entity.Type ?? "unknown"}");
    Console.WriteLine($"    Description: {entity.Description ?? "(none)"}");
    Console.WriteLine($"    Evidence: {entity.Evidence.QuotedText}");
    Console.WriteLine();
}

Console.WriteLine($"Claims: {result.Claims.Length}");
foreach (var claim in result.Claims)
{
    var objectStr = claim.ObjectEntity is not null
        ? $"→ [{claim.ObjectEntity.DefinedNames[0]}]"
        : $"→ \"{claim.ObjectLiteral}\"";

    Console.WriteLine($"  [{claim.Subject.DefinedNames[0]}] {claim.Predicate} {objectStr}");
    Console.WriteLine($"    Modality: {claim.Modality}");
    Console.WriteLine($"    Evidence: {claim.Evidence.QuotedText}");
    Console.WriteLine();
}

var modalityCounts = result.Claims.GroupBy(c => c.Modality).OrderByDescending(g => g.Count());
Console.WriteLine("Claims by modality:");
foreach (var g in modalityCounts)
{
    Console.WriteLine($"  {g.Key,-14} {g.Count(),4}");
}

