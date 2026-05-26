namespace KnowledgeSystem.PluginLoader;

/// <summary>
///     Base interface for plugin application sessions.
///     This should not be implemented directly by plugins; it should be wrapped by the application using this API.
/// </summary>
public interface IPlugin
{
    /// <summary>
    ///     Called by the plugin loader service when the application is starting up.
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    Task Start(CancellationToken token);

    /// <summary>
    ///     Called by the plugin loader service when the application is closing.
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    Task Stop(CancellationToken token);
}