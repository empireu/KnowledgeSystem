using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<ulong, CancellationTokenSource> ActiveRuns = new();
    private static readonly TimeSpan AgentTimeout = TimeSpan.FromMinutes(14);

    public static void CancelAllActiveRuns()
    {
        foreach (var kvp in ActiveRuns)
        {
            kvp.Value.Cancel();
        }
    }

    [SlashCommand("ask", "Ask MQR a single question")]
    public async Task AskAsync([SlashCommandParameter] string message)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        var target = new InteractionMessageTarget(Context.Interaction);
        var observer = factory.Create(target);

        var cts = new CancellationTokenSource(AgentTimeout);
        ActiveRuns[Context.Interaction.Id] = cts;

        _ = RunAgentSafely(
            conversationManager.AskAsync(message, observer, cts.Token),
            target,
            "Agent execution failed in /mqr ask",
            Context.Interaction.Id
        );
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

            var cts = new CancellationTokenSource(AgentTimeout);
            ActiveRuns[thread.Id] = cts;

            _ = RunAgentSafely(
                conversation.RunToCompletionAsync(message, observer, cts.Token),
                target,
                "Agent execution failed in thread {threadId}",
                thread.Id
            );
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

    private async Task RunAgentSafely(Task agentTask, IDiscordMessageTarget target, string logMessage, ulong runKey)
    {
        try
        {
            await agentTask;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Agent task was cancelled for run {RunKey}", runKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, logMessage, runKey);

            try
            {
                await target.UpdateContentAsync($"An error occurred: {ex.Message}");
            }
            catch (Exception discordEx)
            {
                logger.LogError(discordEx, "Discord communication error for run {RunKey}", runKey);
            }
        }
        finally
        {
            ActiveRuns.TryRemove(runKey, out _);
        }
    }
}
