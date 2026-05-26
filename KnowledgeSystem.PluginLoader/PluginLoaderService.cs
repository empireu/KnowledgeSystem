using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.PluginLoader;

/// <summary>
///     The plugin loader service will manage the lifecycle of the plugins.
///     <see cref="IPlugin"/>
/// </summary>
internal sealed class PluginLoaderService(
    ILogger<PluginLoaderService> logger,
    IServiceProvider serviceProvider,
    List<PluginInfo> plugins
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Setting up plugins.");

        foreach (var plugin in plugins)
        {
            logger.LogInformation("Setting up {name} by {author}", plugin.DisplayName, plugin.Author);

            try
            {
                plugin.Instance = (IPlugin)ActivatorUtilities.CreateInstance(serviceProvider, plugin.PluginType);
            }
            catch (Exception e)
            {
                logger.LogError("Failed to instance plugin {name}: {ex}", plugin.QualifiedName, e);
                continue;
            }

            try
            {
                await plugin.Instance.Start(cancellationToken);
            }
            catch (Exception e)
            {
                logger.LogError("Failed to start plugin {name}: {ex}", plugin.QualifiedName, e);
            }
        }

        logger.LogInformation("Finished setting up {count} plugins.", plugins.Count);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var plugin in plugins.Where(plugin => plugin.Instance != null))
        {
            logger.LogInformation("Stopping plugin {name}.", plugin.DisplayName);

            try
            {
                // Pretty big issue if it is null here:
                await plugin.Instance!.Stop(cancellationToken);
            }
            catch (Exception e)
            {
                logger.LogError("Failed to stop plugin {name}: {ex}", plugin.QualifiedName, e);
            }
        }
    }
}