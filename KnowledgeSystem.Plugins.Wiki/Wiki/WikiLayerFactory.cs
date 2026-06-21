using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Wiki.Wiki;

public class WikiLayerFactory(IServiceProvider serviceProvider)
{
    public WikiMessagingLayer CreateMessagingLayer(string name)
    {
        return ActivatorUtilities.CreateInstance<WikiMessagingLayer>(serviceProvider, name);
    }
}