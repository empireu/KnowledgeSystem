using KnowledgeSystem.Api;

namespace KnowledgeSystem.Discord.Conversation;

/// <summary>
///     Manages active Discord-AI conversations.
/// </summary>
public interface IConversationManager
{
    /// <summary>
    ///     Creates a conversation for the given channel. This is a persistent conversation, that is in real-time with user messages.
    /// </summary>
    /// <param name="channelId">The channel. It must be a thread.</param>
    /// <param name="factory">The factory, invoked after validation and the correct state is reached.</param>
    /// <typeparam name="TLayer">The layer implementation.</typeparam>
    /// <returns>The created layer.</returns>
    public TLayer CreateConversation<TLayer>(ulong channelId, Func<IActiveConversation, TLayer> factory) where TLayer : IAgentMessagingLayer;
}