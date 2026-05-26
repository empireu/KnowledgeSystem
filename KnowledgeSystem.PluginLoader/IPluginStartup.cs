using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnowledgeSystem.PluginLoader;

/// <summary>
///     Implemented by plugins that would like to register dependencies in the container and configure the host.
///     This should not be implemented directly by plugins; it should be wrapped by the application using this API.
/// </summary>
public interface IPluginStartup
{
    /// <summary>
    ///     Configures the host builder of the application.
    /// </summary>
    /// <param name="builder"></param>
    void ConfigureHost(IHostBuilder builder);

    /// <summary>
    ///     Configures the service container of the application.
    /// </summary>
    /// <param name="services"></param>
    void ConfigureServices(IServiceCollection services);
}