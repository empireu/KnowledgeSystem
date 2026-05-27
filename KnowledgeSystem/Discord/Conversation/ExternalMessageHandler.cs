using NetCord.Gateway;
using NetCord.Hosting.Gateway;

namespace KnowledgeSystem.Discord.Conversation;

public class ExternalMessageHandler(ConversationManager conversationManager) : IMessageCreateGatewayHandler
{
    public async ValueTask HandleAsync(Message message)
    {
        // Ignore bot messages:
        if (message.Author.IsBot)
        {
            return;
        }

        // Ignore empty messages:
        if (string.IsNullOrWhiteSpace(message.Content))
        {
            return;
        }

        await conversationManager.HandleExternalMessage(message);
    }
}