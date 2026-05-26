using System.Reflection;

namespace KnowledgeSystem.PluginLoader;

internal sealed class PluginInfo(IPluginStartup? startup, Type pluginType, string path, AssemblyName assemblyName)
{
    private readonly PluginAttribute _pluginAttribute = pluginType.GetCustomAttribute<PluginAttribute>()!;
    private readonly List<PluginDependencyAttribute> _dependencyAttributes = pluginType.GetCustomAttributes<PluginDependencyAttribute>().ToList();

    public string DisplayName => _pluginAttribute.DisplayName;
    
    public string Package => _pluginAttribute.Package;
    
    public string Version => _pluginAttribute.Version;
    
    public string Author => _pluginAttribute.Author;
    
    public string QualifiedName => $"{DisplayName} [{Package}]";
    
    public IReadOnlyList<PluginDependencyAttribute> Dependencies => _dependencyAttributes;

    public IPluginStartup? Startup { get; } = startup;
    
    public Type PluginType { get; } = pluginType;
    
    public string Path { get; } = path;
    
    public AssemblyName AssemblyName { get; } = assemblyName;

    /// <summary>
    ///     Populated after the plugin session is set up.
    /// </summary>
    public IPlugin? Instance { get; set; }

    public override string ToString()
    {
        return QualifiedName;
    }
}