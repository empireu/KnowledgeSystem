using KnowledgeSystem.Api;
using KnowledgeSystem.Discord.Integration;

namespace KnowledgeSystem.Discord.Conversation;

/// <summary>
///     Manages active Discord-AI conversations.
/// </summary>
public interface IConversationManager
{
    /// <summary>
    ///     Creates a long-running conversation.
    ///     It will run once the user sends messages in the thread.
    /// </summary>
    public void OpenConversation(
        ulong channelId,
        IAgentMessagingLayer layer
    );

    /// <summary>
    ///     Runs the agent for a single message.
    /// </summary>
    public void RunOneShotConversation(
        ulong interactionId,
        UserMessageInfo messageInfo,
        IDiscordMessageTarget target,
        IAgentMessagingLayer layer
    );
}