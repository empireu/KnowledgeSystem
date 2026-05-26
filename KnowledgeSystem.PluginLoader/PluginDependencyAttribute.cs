namespace KnowledgeSystem.PluginLoader;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class PluginDependencyAttribute : Attribute
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
    ///     Defines a dependency on another plugin.
    /// </summary>
    /// <param name="packageName">The package to depend on. It is the one passed to the plugin attribute.</param>
    /// <param name="type"></param>
    public PluginDependencyAttribute(string packageName, DependencyType type = DependencyType.Required)
    {
        PackageName = packageName;
        Type = type;
    }

    public string PackageName { get; }
    public DependencyType Type { get; }
}