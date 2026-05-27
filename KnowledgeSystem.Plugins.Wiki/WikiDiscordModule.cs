using KnowledgeSystem.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace KnowledgeSystem.Plugins.Wiki;

public class MqrModule(ILogger<MqrModule> logger, IConversationManager conversationManager, IServiceProvider serviceProvider ) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("ask", "Ask MQR a single question")]
    public async Task AskAsync([SlashCommandParameter] string message)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
        
        var messagingLayer = ActivatorUtilities.CreateInstance<WikiMessagingLayer>(
            serviceProvider,
            "wiki_ask"
        );
       
        conversationManager.RunOneShotConversation(
            Context.Interaction.Id,
            new UserMessageInfo(message),
            new InteractionMessageTarget(Context.Interaction),
            messagingLayer
        );
    }

    [SlashCommand("unleash", "Start a persistent MQR conversation thread")]
    public async Task UnleashAsync([SlashCommandParameter] string message)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
        
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

        var messagingLayer = ActivatorUtilities.CreateInstance<WikiMessagingLayer>(
            serviceProvider,
            "wiki_convo"
        );
            
        conversationManager.OpenConversation(thread.Id, messagingLayer);

        await Context.Interaction.ModifyResponseAsync(m => m.Content = $"Thread created! <#{thread.Id}>");
    }
}