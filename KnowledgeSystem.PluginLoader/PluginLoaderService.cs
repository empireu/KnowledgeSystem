using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.PluginLoader;

/// <summary>
///     The plugin loader service will manage the lifecycle of the plugins.
///     <see cref="IPlugin"/>
/// </summary>
internal sealed class PluginLoaderService : IHostedService
{
    private readonly ILogger<PluginLoaderService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly List<PluginInfo> _plugins;

    public PluginLoaderService(ILogger<PluginLoaderService> logger, IServiceProvider serviceProvider, List<PluginInfo> plugins)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _plugins = plugins;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Setting up plugins.");

        foreach (var plugin in _plugins)
        {
            _logger.LogInformation("Setting up {name} by {author}", plugin.DisplayName, plugin.Author);

            try
            {
                plugin.Instance = (IPlugin)ActivatorUtilities.CreateInstance(_serviceProvider, plugin.PluginType);
            }
            catch (Exception e)
            {
                _logger.LogError("Failed to instance plugin {name}: {ex}", plugin.QualifiedName, e);
                continue;
            }

            try
            {
                await plugin.Instance.Start(cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError("Failed to start plugin {name}: {ex}", plugin.QualifiedName, e);
            }
        }

        _logger.LogInformation("Finished setting up {count} plugins.", _plugins.Count);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var plugin in _plugins.Where(plugin => plugin.Instance != null))
        {
            _logger.LogInformation("Stopping plugin {name}.", plugin.DisplayName);

            try
            {
                // Pretty big issue if it is null here:
                await plugin.Instance!.Stop(cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError("Failed to stop plugin {name}: {ex}", plugin.QualifiedName, e);
            }
        }
    }
}