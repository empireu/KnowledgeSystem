using KnowledgeSystem.Discord.Conversation;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

// ReSharper disable UnusedMember.Global

namespace KnowledgeSystem.Discord;

public class MqrModule(
    ILogger<MqrModule> logger,
    IConversationManager conversationManager,
    DiscordObserverFactory factory
) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("ask", "Ask MQR a single question")]
    public async Task AskAsync([SlashCommandParameter] string message)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        var target = new InteractionMessageTarget(Context.Interaction);
        var observer = factory.Create(target);

        // The observer handles all message updates. TODO this is sort of a crappy pattern
        _ = Task.Run(async () =>
        {
            try
            {
                await conversationManager.AskAsync(message, observer, CancellationToken.None);
            }
            catch (Exception conversationError)
            {
                logger.LogError(conversationError, "Background agent execution failed in /mqr ask");
                
                try
                {
                    await target.UpdateContentAsync($"An error occurred: {conversationError.Message}");
                }
                catch(Exception exD)
                {
                    logger.LogError(exD, "Discord error occurred");
                }
            }
        });
    }

    [SlashCommand("unleash", "Start a persistent AI conversation thread")]
    public async Task UnleashAsync([SlashCommandParameter] string message)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        try
        {
            if (Context.Channel is not TextGuildChannel textChannel)
            {
                await Context.Interaction.ModifyResponseAsync(m => m.Content = "This command can only be used in a server text channel.");
                return;
            }

            // Text channels require threads to be created from a message.
            // This message becomes the thread's root/anchor.
            var threadName = $"MQR: {Context.User.Username}";
            var starterMessage = await textChannel.SendMessageAsync(new MessageProperties
            {
                Content = $"▸ **{Context.User.Username}** started a conversation"
            });
            
            var thread = await textChannel.CreateGuildThreadAsync(starterMessage.Id, new GuildThreadFromMessageProperties(threadName));

            // Register the conversation immediately so MessageHandler picks up replies:
            var conversation = conversationManager.CreateConversation(thread.Id);

            await Context.Interaction.ModifyResponseAsync(m => m.Content = $"Thread created! <#{thread.Id}>");

            // Send a status message in the thread that the observer will modify in-place:
            var statusMessage = await Context.Client.Rest.SendMessageAsync(thread.Id, new MessageProperties
            {
                Content = "> ▸ *Unleashing...*"
            });

            var target = new ChannelMessageTarget(Context.Client.Rest, thread.Id, statusMessage.Id);
            var observer = factory.Create(target);
            
            // TODO this is sort of a crappy pattern
            _ = Task.Run(async () =>
            {
                try
                {
                    await conversation.RunToCompletionAsync(message, observer, CancellationToken.None);
                }
                catch (Exception agentError)
                {
                    logger.LogError(agentError, "Background agent execution failed in thread {thread}", thread.Id);
                 
                    try
                    {
                        await target.UpdateContentAsync($"An error occurred: {agentError.Message}");
                    }
                    catch(Exception exD)
                    {
                        logger.LogError(exD, "Discord communication error");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in /mqr unleash");
            try
            {
                await Context.Interaction.ModifyResponseAsync(m => m.Content = $"An error occurred: {ex.Message}");
            }
            catch
            {
                // Interaction may have already been updated
            }
        }
    }
}
