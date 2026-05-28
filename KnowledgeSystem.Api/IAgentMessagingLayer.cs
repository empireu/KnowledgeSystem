namespace KnowledgeSystem.Api;

/// <summary>
///     API layer for a single conversation or a one-shot command.
/// </summary>
public interface IAgentMessagingLayer
{
    /// <summary>
    ///     Called only once, before the first message comes through.
    /// </summary>
    public Task PrepareAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Creates a response pipeline for the user's message.
    /// </summary>
    /// <param name="messageTarget">The message integration layer.</param>
    /// <param name="userMessageInfo">Data containing the message.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IResponsePipeline> CreateResponsePipeline(IDiscordMessageTarget messageTarget, UserMessageInfo userMessageInfo, CancellationToken cancellationToken);

    /// <summary>
    ///     Called when the layer is detached (it will never receive any more messages).
    /// </summary>
    public Task CloseAsync(LayerCloseReason reason, CancellationToken cancellationToken) => Task.CompletedTask;
}