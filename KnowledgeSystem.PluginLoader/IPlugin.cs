namespace KnowledgeSystem.PluginLoader;

/// <summary>
///     Base interface for plugin application sessions.
/// </summary>
public interface IPlugin
{
    /// <summary>
    ///     Called by the plugin loader service when the application is starting up.
    /// </summary>
    public Task Start(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     Called by the plugin loader service when the application is closing.
    /// </summary>
    public Task Stop(CancellationToken cancellationToken) => Task.CompletedTask;
}