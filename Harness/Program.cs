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

const string testContent = """
                           Naomi talked in her sleep. It was one of a dozen things Holden hadn’t known about her before tonight. Even though they’d slept in crash couches a few feet apart on many occasions, he’d never heard it. Now, with her face against his bare chest, he could feel her lips move and the soft, punctuated exhalations of her words. He couldn’t hear what she was saying.
                           She also had a scar on her back, just above her left buttock. It was three inches long and had the uneven edges and rippling that came from a tear rather than a slice. Naomi would never get herself knifed in a bar fight, so it had to have come on the job. Maybe she had been climbing through tight spaces in the engine room when the ship maneuvered unexpectedly. A competent plastic surgeon could have made it invisible in one visit. That she hadn’t bothered and clearly didn’t care was another thing he had learned about her tonight.
                           She stopped murmuring and smacked her lips a few times, then said, “Thirsty.”
                           Holden slid out from under her and headed for the kitchen, knowing that this was the obsequiousness that always accompanied a new lover. For the next couple of weeks, he wouldn’t be able to stop himself from fulfilling every whim Naomi might have. It was a behavior some men carried at the genetic level, their DNA wanting to make sure that first time wasn’t just a fluke.
                           Her room was laid out differently than his, and the unfamiliarity made him clumsy in the dark. He fumbled around for a few minutes in her small kitchen nook, looking for a glass. By the time he found it, filled it, and headed back into the bedroom, Naomi was sitting up in bed. The sheet lay pooled on her lap. The sight of her half nude in the dimly lit room gave him an embarrassingly sudden erection.
                           Naomi panned her gaze up his body, pausing at his midsection, then at the water glass, and said, “Is that for me?”
                           Holden didn’t know which thing she was asking about, so he just said, “Yes.”
                           """;

var chunk = new IngestionChunkSource(testContent, []);
var result = await pipeline.IngestAsync(chunk);

Console.WriteLine("▔▔▔▔▔▔ Entities ▔▔▔▔▔▔");
foreach (var entity in result.Entities)
{
    Console.WriteLine($"  Name: {string.Join(", ", entity.DefinedNames)}");
    Console.WriteLine($"  Type: {entity.Type ?? "(none)"}");
    Console.WriteLine($"  Description: {entity.Description ?? "(none)"}");
    Console.WriteLine($"  Evidence: \"{entity.Evidence.QuotedText}\"");
    if (entity.Evidence.Span is { } span)
    {
        Console.WriteLine($"  Span: [{span.Start}..{span.EndExclusive})");
    }

    Console.WriteLine();
}

Console.WriteLine("▔▔▔▔▔▔ Relationships ▔▔▔▔▔▔");
foreach (var rel in result.Relationships)
{
    Console.WriteLine($"  {rel.Source.DefinedNames[0]} --[{rel.ActionDescription}]--> {rel.Target.DefinedNames[0]}");
    Console.WriteLine($"  Evidence: \"{rel.Evidence.QuotedText}\"");
    if (rel.Evidence.Span is { } span)
    {
        Console.WriteLine($"  Span: [{span.Start}..{span.EndExclusive})");
    }

    Console.WriteLine();
}

Console.WriteLine("▔▔▔▔▔▔ Attributes ▔▔▔▔▔▔");
foreach (var attr in result.Attributes)
{
    Console.WriteLine($"  {attr.EntityName}.{attr.AttributeName} = {attr.Value}");
    Console.WriteLine($"  Evidence: \"{attr.Evidence.QuotedText}\"");
    if (attr.Evidence.Span is { } span)
    {
        Console.WriteLine($"  Span: [{span.Start}..{span.EndExclusive})");
    }

    Console.WriteLine();
}

Console.WriteLine("Done.");
