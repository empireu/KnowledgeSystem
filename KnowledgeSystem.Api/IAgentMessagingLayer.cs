using KnowledgeSystem.Discord.Integration;
using NetCord.Rest;

namespace KnowledgeSystem.Api;

/// <summary>
///     API layer for a single conversation or a one-shot command.
/// </summary>
public interface IAgentMessagingLayer
{
    /// <summary>
    ///     Called only once, before the conversation starts.
    ///     The context and other long-living objects should be created here, with the system prompt inserted (if applicable).
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task PrepareAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Creates a response pipeline for the user's message.
    /// </summary>
    /// <param name="messageTarget">The message integration layer.</param>
    /// <param name="userMessageInfo">Data containing the message.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IResponsePipeline> CreateResponsePipeline(IDiscordMessageTarget messageTarget, UserMessageInfo userMessageInfo, CancellationToken cancellationToken);
}