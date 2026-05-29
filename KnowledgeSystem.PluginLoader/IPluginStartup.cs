using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnowledgeSystem.PluginLoader;

/// <summary>
///     Implemented by plugins that would like to register dependencies in the container and configure the host.
/// </summary>
public interface IPluginStartup
{
    /// <summary>
    ///     Configures the host builder of the application.
    /// </summary>
    public void ConfigureHost(IHostBuilder builder) { }

    /// <summary>
    ///     Configures the service container of the application.
    /// </summary>
    public void ConfigureServices(HostBuilderContext context, IServiceCollection services) { }
}