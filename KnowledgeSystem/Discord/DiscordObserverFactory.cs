using KnowledgeSystem.Discord.Observer;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Discord;

public class DiscordObserverFactory(IServiceProvider serviceProvider)
{
    public DiscordObserver Create(IDiscordMessageTarget target)
    {
        return ActivatorUtilities.CreateInstance<DiscordObserver>(serviceProvider, target);
    }
}