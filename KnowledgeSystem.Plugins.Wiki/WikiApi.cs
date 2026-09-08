using KnowledgeSystem.Events.Api;
using KnowledgeSystem.Events.Implementation;
using KnowledgeSystem.Plugins.Wiki.Wiki;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Plugins.Wiki;

public sealed class WikiAgentCreateEvent(WikiAgent agent) : IEvent;

public sealed class WikiApi(IServiceProvider serviceProvider)
{
    internal readonly DefaultEventManager EventManagerInternal = new(
        serviceProvider.GetRequiredService<ILogger<DefaultEventManager>>(),
        serviceProvider
    );

    public IEventReceiverRegistry Events => EventManagerInternal;
}