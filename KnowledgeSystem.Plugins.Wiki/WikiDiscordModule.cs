using KnowledgeSystem.Api;
using KnowledgeSystem.Plugins.Wiki.Wiki;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

// ReSharper disable UnusedMember.Global

namespace KnowledgeSystem.Plugins.Wiki;

public class WikiModule(IConversationManager conversationManager, WikiLayerFactory factory) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("ask", "Ask a single wiki question")]
    public async Task AskAsync([SlashCommandParameter] string message)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        var messagingLayer = factory.CreateMessagingLayer("wiki_ask");
       
        conversationManager.RunOneShotConversation(
            Context.Interaction.Id,
            new UserMessageInfo(message, Context.User.Username),
            new InteractionMessageTarget(Context.Interaction),
            messagingLayer
        );
    }

    [SlashCommand("unleash", "Start a persistent wiki conversation thread")]
    public async Task UnleashAsync([SlashCommandParameter] string topic)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
        
        if (Context.Channel is not TextGuildChannel textChannel)
        {
            await Context.Interaction.ModifyResponseAsync(m => m.Content = "This command can only be used in a server text channel.");
            return;
        }

        // Text channels require threads to be created from a message.
        // This message becomes the thread's root/anchor.
        var threadName = $"{Context.User.Username}/{topic}";
        var starterMessage = await textChannel.SendMessageAsync(new MessageProperties
        {
            Content = $"▸ {topic}"
        });
            
        var thread = await textChannel.CreateGuildThreadAsync(starterMessage.Id, new GuildThreadFromMessageProperties(threadName));

        var messagingLayer = factory.CreateMessagingLayer("wiki_conversation");
            
        conversationManager.OpenConversation(thread.Id, messagingLayer);

        await Context.Interaction.DeleteResponseAsync();
    }
}