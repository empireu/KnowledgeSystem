using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Plugins.CodeMemory.Memory;

namespace KnowledgeSystem.Plugins.CodeMemory.Agent;

public sealed class RecallAgent : Agent<RecallContext>
{
    public RecallAgent(string @namespace, string agentId, IServiceProvider serviceProvider) : base(agentId)
    {
        SearchMemoriesToolHandler<RecallContext>.Register(@namespace, ToolRegistry, serviceProvider);
        ExportMemoriesToolHandler.Register(ToolRegistry);
    }
}