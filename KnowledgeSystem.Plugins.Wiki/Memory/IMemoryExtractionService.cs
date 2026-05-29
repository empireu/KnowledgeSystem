namespace KnowledgeSystem.Plugins.Wiki.Memory;

public interface IMemoryExtractionService
{
    /// <summary>
    ///     Posts a chat for memory extraction.
    /// </summary>
    public Task EnqueueChatAsync(PendingChat chat, CancellationToken cancellationToken);
}