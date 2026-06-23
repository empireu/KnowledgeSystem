using KnowledgeSystem.Plugins.Library;

namespace KnowledgeSystem.Plugins.CodeMemory.Agent;

public class RecallContext : BasicContext
{
    public readonly List<int> MarkedMemories = [];
}