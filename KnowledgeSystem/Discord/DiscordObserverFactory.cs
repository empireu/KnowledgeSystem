using KnowledgeSystem.Discord.Observer;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Discord;

public class DiscordObserverFactory(IServiceProvider serviceProvider)
{
    public DiscordObserver Create(IDiscordMessageTarget target)
    {
        return ActivatorUtilities.CreateInstance<DiscordObserver>(serviceProvider, target);
    }

    public DiscordObserver2 CreateV2(IDiscordMessageTarget target)
    {
        return ActivatorUtilities.CreateInstance<DiscordObserver2>(serviceProvider, target);
    }
}