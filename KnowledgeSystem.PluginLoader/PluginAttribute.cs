namespace KnowledgeSystem.PluginLoader;

/// <summary>
///     Classes that wish to implement a plugin must be decorated with an inheritor of this class.
///     This should not be implemented directly by plugins; it should be wrapped by the application using this API.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public abstract class PluginAttribute: Attribute
{
    /// <summary>
    ///     Creates a new instance of the PluginAttribute class.
    /// </summary>
    /// <param name="displayName">The name of the plugin that is displayed to users.</param>
    /// <param name="package">The package name of the plugin. It may be used to reference this plugin from other plugins.</param>
    /// <param name="version">The version of this plugin. No convention is enforced.</param>
    /// <param name="author">The author of this plugin.</param>
    protected PluginAttribute(string displayName, string package, string version, string author)
    {
        DisplayName = displayName;
        Package = package;
        Version = version;
        Author = author;
    }

    public string DisplayName { get; }
    public string Package { get; }
    public string Version { get; }
    public string Author { get; }
}