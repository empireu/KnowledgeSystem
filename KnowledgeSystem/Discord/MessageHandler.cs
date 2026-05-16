using KnowledgeSystem.Discord.Conversation;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;

namespace KnowledgeSystem.Discord;

public class MessageHandler(
    ILogger<MessageHandler> logger,
    IConversationManager conversationManager,
    DiscordObserverFactory factory,
    RestClient restClient
) : IMessageCreateGatewayHandler
{
    public async ValueTask HandleAsync(Message message)
    {
        // Ignore bot messages:
        if (message.Author.IsBot)
        {
            return;
        }

        // Only handle messages in active conversation threads:
        if (!conversationManager.HasConversation(message.ChannelId))
        {
            return;
        }

        // Ignore empty messages:
        if (string.IsNullOrWhiteSpace(message.Content))
        {
            return;
        }

        logger.LogInformation(
            "Processing message in conversation {channel} from {user}: {content}",
            message.ChannelId,
            message.Author.Username,
            message.Content
        );

        try
        {
            // Send an initial status message that the observer will modify in-place
            var statusMessage = await restClient.SendMessageAsync(message.ChannelId, new MessageProperties
            {
                Content = "> ▸ *Observing...*"
            });

            var target = new ChannelMessageTarget(restClient, message.ChannelId, statusMessage.Id);
            var observer = factory.Create(target);
            var conversation = conversationManager.GetChannelConversation(message.ChannelId);
            await conversation.RunToCompletionAsync(message.Content, observer);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing message in conversation {channel}", message.ChannelId);

            try
            {
                await restClient.SendMessageAsync(
                    message.ChannelId,
                    new MessageProperties
                    {
                        Content = "An error occurred while processing your message."
                    }
                );
            }
            catch (Exception restEx)
            {
                logger.LogError(restEx, "Discord communication failure");
            }
        }
    }
}
