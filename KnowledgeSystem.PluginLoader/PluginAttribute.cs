namespace KnowledgeSystem.PluginLoader;

[AttributeUsage(AttributeTargets.Class)]
public sealed class PluginAttribute : Attribute
{
    /// <summary>
    ///     Marks a plugin class and defines some information about the plugin implemented.
    /// </summary>
    /// <param name="displayName">The name of the plugin that is displayed to users.</param>
    /// <param name="package">The package name of the plugin. It may be used to reference this plugin from other plugins.</param>
    /// <param name="version">The version of this plugin. No convention is enforced.</param>
    /// <param name="author">The author of this plugin.</param>
    public PluginAttribute(string displayName, string package, string version, string author)
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