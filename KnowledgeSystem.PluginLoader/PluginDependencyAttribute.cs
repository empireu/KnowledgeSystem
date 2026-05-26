namespace KnowledgeSystem.PluginLoader;

/// <summary>
///     Defines a dependency on another plugin.
///     This should not be implemented directly by plugins; it should be wrapped by the application using this API.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public abstract class PluginDependencyAttribute : Attribute
{
    public enum DependencyType
    {
        /// <summary>
        ///     The plugin can still function without the dependency plugin.
        /// </summary>
        Optional,
        /// <summary>
        ///     The plugin cannot be loaded without the dependency plugin.
        /// </summary>
        Required
    }

    /// <summary>
    ///     Creates a new instance of the PackageDependencyAttribute class.
    /// </summary>
    /// <param name="packageName">The package to depend on. It is the one passed to the plugin attribute.</param>
    /// <param name="type"></param>
    protected PluginDependencyAttribute(string packageName, DependencyType type = DependencyType.Required)
    {
        PackageName = packageName;
        Type = type;
    }

    public string PackageName { get; }
    public DependencyType Type { get; }
}