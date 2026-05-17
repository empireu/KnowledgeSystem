using KnowledgeSystem.Agents.Context.TokenEstimation;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Observer;

namespace KnowledgeSystem.Discord.Conversation;

/// <summary>
///     Manages active Discord-AI conversations.
/// </summary>
public interface IConversationManager : AgentRunner.ICompletionFactory
{
    public ITokenEstimator TokenEstimator { get; }
    
    /// <summary>
    ///     Checks if the manager has a conversation created for the supplied thread channel.
    /// </summary>
    public bool HasConversation(ulong channelId);

    /// <summary>
    ///     Gets the conversation for the given channel.
    /// </summary>
    public ActiveConversation GetChannelConversation(ulong channelId);

    /// <summary>
    ///     Gets the conversation for the given channel, if it exists.
    /// </summary>
    public ActiveConversation? TryGetConversation(ulong channelId);

    /// <summary>
    ///     Creates a conversation for the given channel.
    /// </summary>
    public ActiveConversation CreateConversation(ulong channelId);
    
    /// <summary>
    ///     Removes the conversation for the given channel.
    /// </summary>
    public void RemoveConversation(ulong channelId);

    /// <summary>
    ///     Refreshes the timeout for the conversation on the given channel.
    /// </summary>
    public void TouchConversation(ulong channelId);

    /// <summary>
    ///     Closes all active conversations with the given reason message.
    /// </summary>
    public Task CloseAllAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Asks a single question and returns the answer. Used for one-shot calls.
    /// </summary>
    public Task AskAsync(string message, IAgentObserver observer, CancellationToken cancellationToken = default);
}