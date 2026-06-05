using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Ai;
using KnowledgeSystem.Embedding;
using KnowledgeSystem.Events.Implementation;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.Surveillance.Agent;
using KnowledgeSystem.Plugins.Surveillance.Database;
using KnowledgeSystem.Retrieval.Graph.Extraction;
using Microsoft.Extensions.DependencyInjection;

var dbPath = "ingestion.db";

using var canonicalDb = new CanonicalDbContext(dbPath);
canonicalDb.InitializeSchema();

using var embeddingService = new OpenAiEmbeddingService(
    "http://127.0.0.1:1234/v1",
    "None",
    "text-embedding-mxbai-embed-large-v1",
    1024,
    "Represent this sentence for searching relevant passages: "
);

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

var services = new ServiceCollection();
services.AddSingleton(canonicalDb);
services.AddSingleton<IEmbeddingService>(embeddingService);
var sp = services.BuildServiceProvider();

var agent = new KnowledgeGraphAgent("kg", sp);

var context = new BasicContext();
context.Timeline.InsertSystem("""
    You are a knowledge graph query agent. You have access to a database of entities and claims extracted from Discord conversations about the game Space Engineers, on the Sigma Draconis Expanse (SDX) server.

    Use the available tools to answer the user's questions:
    - search_entities: Find entities by name, description, or type.
    - search_claims: Search claims with structured filters (subject, object, modality, date range, text search, semantic search).
    - get_entity_details: Get statistics and context about an entity before querying its claims.

    Answer concisely. Use evidence from claims to support your answers.
    """);

context.Timeline.InsertUser("I need to know what stupid things Triple said");

var runner = new AgentRunner<BasicContext>(
    client: client,
    agent: agent,
    parent: null,
    context: context,
    eventManager: eventManager,
    cancellationToken: CancellationToken.None,
    completionFactory: null
);

var turn = 0;

while (!runner.IsFinished)
{
    if (turn > 10)
    {
        Console.WriteLine("Turn limit exceeded.");
        break;
    }

    var status = await runner.ExecuteTurn();

    switch (status)
    {
        case AgentRunner.TurnStatus.ToolCallsReceived:
        case AgentRunner.TurnStatus.CompletedSuccessfully:
        case AgentRunner.TurnStatus.CompletedWithError:
        case AgentRunner.TurnStatus.CompletionHandled:
            turn++;
            break;
    }
}

Console.WriteLine($"\nDone in {turn} turns.");
