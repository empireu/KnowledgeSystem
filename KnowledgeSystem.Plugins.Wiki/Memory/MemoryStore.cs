using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Plugins.Wiki.Memory;

public sealed class MemoryStore(ILogger<MemoryStore> logger)
{
    public async Task<string> CreateMemory(string summary, string content, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating memory: {summary} | {content}", summary, content);
        
        return Guid.NewGuid().ToString();
    }
}