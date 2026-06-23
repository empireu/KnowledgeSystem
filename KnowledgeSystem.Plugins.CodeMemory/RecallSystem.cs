using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Ai;
using KnowledgeSystem.Events.Implementation;
using KnowledgeSystem.Plugins.CodeMemory.Agent;
using KnowledgeSystem.Plugins.CodeMemory.Memory;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Plugins.CodeMemory;

public class RecallSystem
{
    private readonly ILogger<RecallSystem> _logger;
    private readonly MemoryStoreService _memoryStore;
    private readonly MemorySystemConfig _memoryConfig;
    private readonly IServiceProvider _serviceProvider;
    private readonly IChatClient _recallClient;
    private readonly string _systemPrompt;

    public RecallSystem(
        ILogger<RecallSystem> logger,
        IOptions<MemorySystemConfig> options, 
        MemoryStoreService memoryStore,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _memoryStore = memoryStore;
        _memoryConfig = options.Value;
        _serviceProvider = serviceProvider;
        _recallClient = OpenAiChatClientFactory.Create(_memoryConfig.RecallProvider);
        _systemPrompt = File.ReadAllText(options.Value.RecallSystemPrompt);
    }

    public async Task<string> RecallAsync(string @namespace, string query, CancellationToken cancellationToken)
    {
        var recallContext = new RecallContext();
        recallContext.Timeline.InsertSystem(_systemPrompt);
        recallContext.Timeline.InsertUser(query);
        
        var agent = new RecallAgent(
            @namespace,
            "recall",
            _serviceProvider
        );
        
        var runner = new AgentRunner<RecallContext>(
            _recallClient, 
            agent,
            null,
            recallContext,
            NullEventManager.Instance,
            cancellationToken,
            AgentRunner.ICompletionFactory.Wrap(_memoryConfig.Recall.CreateOptions)
        );

        var error = await ExecuteAsync(runner);

        if (error != null)
        {
            return error;
        }

        if (recallContext.MarkedMemories.Count == 0)
        {
            return "No relevant memories found.";
        }
        
        var memories = await _memoryStore.GetMemoriesAsync(recallContext.MarkedMemories, cancellationToken);

        var sb = new StringBuilder();
        for (var index = 0; index < memories.Count; index++)
        {
            var memory = memories[index];
            if (memory == null)
            {
                _logger.LogWarning("Got invalid memory {id} from recall agent", recallContext.MarkedMemories[index]);
                continue;
            }

            sb.AppendLine($"# Memory ID {memory.Id}");
            sb.AppendLine(memory.Content);
            sb.AppendLine("---");
        }

        var logSummary = string.Join(", ", memories
            .Where(m => m != null)
            .Select(m => $"{m!.Id}: {m.Summary}"));
        
        _logger.LogInformation("For query \"{query}\", recalled: {memories}", query, logSummary);

        return sb.ToString();
    }

    private async Task<string?> ExecuteAsync(AgentRunner<RecallContext> runner)
    {
        for (var turn = 0;; turn++)
        {
            if (turn == 15)
            {
                _logger.LogError("Hit {number} turns for recall agent. Dropping operation", turn);
                return "Recall system hit maximum turn limit";
            }
            
            var status = await runner.ExecuteTurn();

            if (status == AgentRunner.TurnStatus.CompletedWithError)
            {
                _logger.LogError("Recall completed with error {error}", runner.FinishError);
                return $"Recall system finished with an internal error ({runner.FinishError})";
            }

            if (status == AgentRunner.TurnStatus.CompletedSuccessfully || _memoryConfig.RunForOneTurn)
            {
                return null;
            }
        }
    }
}