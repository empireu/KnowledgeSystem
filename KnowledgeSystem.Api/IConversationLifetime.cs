using KnowledgeSystem.Discord.Integration;

namespace KnowledgeSystem.Api;

/// <summary>
///     API layer for a single conversation or a one-shot command.
/// </summary>
public interface IConversationLifetime
{
    /// <summary>
    ///     Called only once, before the conversation starts.
    ///     The context and other long-living objects should be created here, with the system prompt inserted (if applicable).
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task PrepareAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Responds to the user's message.
    /// </summary>
    /// <param name="messageTarget">The message integration layer.</param>
    /// <param name="userMessageInfo">Data containing the message.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task RespondToUserAsync(IDiscordMessageTarget messageTarget, UserMessageInfo userMessageInfo, CancellationToken cancellationToken);

    /// <summary>
    ///     Called when the conversation is destroyed (either due to timeout, or completion, in the case of one-shot queries).
    /// </summary>
    /// <returns></returns>
    public Task Destroy() => Task.CompletedTask;
}