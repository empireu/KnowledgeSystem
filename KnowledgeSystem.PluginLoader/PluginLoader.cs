using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace KnowledgeSystem.PluginLoader;

public static class PluginLoader
{
    private static readonly ILogger Logger = Log.ForContext(typeof(PluginLoader));

    /// <summary>
    ///     Encapsulates information about a plugin/library assembly and acts as a deferred loader (lazy).
    /// </summary>
    private class AssemblyLazy(AssemblyName assemblyName, string path, bool isPlugin)
    {
        public AssemblyName AssemblyName { get; } = assemblyName;
        public string Path { get; } = path;
        public bool IsPlugin { get; } = isPlugin;

        private Assembly? _instance;

        /// <summary>
        ///     Loads and stores the assembly if it is not loaded, and returns the assembly instance.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public Assembly GetOrLoad(AssemblyLoadContext context)
        {
            if (_instance == null)
            {
                using var stream = File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.Read);

                _instance = context.LoadFromStream(stream);
            }

            return _instance;
        }
    }

    public readonly struct PluginDirectories(string pluginsDirectory, string librariesDirectory)
    {
        public static readonly PluginDirectories Default = new("plugins", "libraries");

        /// <summary>
        ///     Gets the directory to search for plugins.
        /// </summary>
        public string PluginsDirectory { get; } = pluginsDirectory;

        /// <summary>
        ///     Gets the directory to search for extra plugin dependencies.
        /// </summary>
        public string LibrariesDirectory { get; } = librariesDirectory;
    }

    public readonly struct PluginLoaderOptions(PluginDirectories directories, string[] excludePostFixes)
    {
        public static readonly PluginLoaderOptions Default = new(PluginDirectories.Default, []);

        public PluginDirectories Directories { get; } = directories;

        /// <summary>
        ///     Gets the list of file names to exclude from the scan.
        ///     These could be assemblies that shouldn't be loaded, but are allowed to exist in the directory (e.g. the plugin API assembly, which would already be loaded in)
        /// </summary>
        public string[] ExcludePostFixes { get; } = excludePostFixes;
    }

    private static void EnsureDirectoriesExist(PluginDirectories options)
    {
        if (!Directory.Exists(options.LibrariesDirectory))
        {
            Directory.CreateDirectory(options.LibrariesDirectory);
        }

        if (!Directory.Exists(options.PluginsDirectory))
        {
            Directory.CreateDirectory(options.PluginsDirectory);
        }
    }

    public static IHostBuilder UsePluginLoader(this IHostBuilder builder, PluginLoaderOptions options) 
    {
        var context = AssemblyLoadContext.Default;
        var directories = options.Directories;

        EnsureDirectoriesExist(directories);

        var pluginAssemblyDescriptors = ProbeAssemblies(directories.PluginsDirectory, true, options.ExcludePostFixes);
        var libraryAssemblyDescriptors = ProbeAssemblies(directories.LibrariesDirectory, false, options.ExcludePostFixes);

        var assemblies = pluginAssemblyDescriptors.Concat(libraryAssemblyDescriptors).ToArray();

        // Map name->assembly
        var assemblyLookup = assemblies
            .Where(a => !string.IsNullOrEmpty(a.AssemblyName.Name))
            .ToDictionary(a => a.AssemblyName.Name!);

        // Set up dependency loading

        context.Resolving += (loadContext, assemblyName) =>
        {
            Logger.Verbose("Loading assembly {name} {version}", assemblyName.Name, assemblyName.Version);

            if (assemblyName.Name == null || !assemblyLookup.TryGetValue(assemblyName.Name, out var inf))
            {
                return null;
            }

            return inf.GetOrLoad(loadContext);
        };

        var pluginAssemblies = assemblies
            .Where(a => a.IsPlugin)
            .Select(a => (context.LoadFromAssemblyName(a.AssemblyName), a.Path))
            .ToList();

        Logger.Information("Loaded {0} plugin assemblies", pluginAssemblies.Count);

        var pluginInformation = new List<PluginInfo>();

        foreach (var (pluginAssembly, pluginPath) in pluginAssemblies)
        {
            // Get the type for the startup class:
            var startupCandidates = pluginAssembly
                .GetTypes()
                .Where(x => typeof(IPluginStartup).IsAssignableFrom(x) && x.IsClass)
                .ToList();

            if (startupCandidates.Count > 1)
            {
                Logger.Error("Plugin {0} contains more than 1 startup! Skipped loading.", pluginAssembly.Location);

                continue;
            }

            // get the type for the plugin class
            var pluginCandidates = pluginAssembly
                .GetTypes()
                .Where(x =>
                    typeof(IPlugin).IsAssignableFrom(x)
                    && x is { IsClass: true, IsAbstract: false } 
                    && x.GetCustomAttribute<PluginAttribute>() != null)
                .ToList();

            if (pluginCandidates.Count != 1)
            {
                Logger.Error("Plugin {0} must have 1 plugin implementation, but it has {1}! Skipped loading.", pluginAssembly.Location, pluginCandidates.Count);
                continue;
            }

            pluginInformation.Add(
                new PluginInfo(
                    startupCandidates
                        .Select(Activator.CreateInstance)   // Create instance or null
                        .Cast<IPluginStartup>() 
                        .FirstOrDefault(),
                    pluginCandidates.First(),               // Unique plugin instance
                    pluginPath, 
                    pluginAssembly.GetName()));
        }

        var orderedPlugins = CreateDependencyOrderedSet(pluginInformation);

        foreach (var pluginInfo in orderedPlugins)
        {
            Logger.Information("Configuring host for {p}", pluginInfo);
            pluginInfo.Startup?.ConfigureHost(builder);
        }

        builder.ConfigureServices((_, services) =>
        {
            services.AddHostedService(provider => ActivatorUtilities.CreateInstance<PluginLoaderService>(provider, orderedPlugins));

            foreach (var plugin in orderedPlugins)
            {
                plugin.Startup?.ConfigureServices(services);
            }
        });

        return builder;
    }


    /// <summary>
    ///     Probes the directory for candidate DLL files (libraries and plugins).
    /// </summary>
    /// <param name="directory">The directory to probe. It must exist, otherwise, an error will be produced.</param>
    /// <param name="isPlugin">Plugin flag that is passed to the <see cref="AssemblyLazy"/></param>
    /// <param name="exclude">File post-fixes to exclude.</param>
    /// <returns>The list of all assemblies in the specified directory.</returns>
    private static List<AssemblyLazy> ProbeAssemblies(string directory, bool isPlugin, string[] exclude)
    {
        // We want to scan for all candidates (basically, assembly DLL files)

        var files = Directory.EnumerateFiles(directory)
            .Where(p => p.EndsWith(".dll"))
            .Where(p => !exclude.Contains(p))
            .ToList();

        var results = new List<AssemblyLazy>();

        // Parallelism actually helps when there are a lot of files to probe.
        // It scaled up pretty well in my tests.
        Parallel.ForEach(files, file =>
        {
            AssemblyName assemblyName;

            try
            {
                assemblyName = AssemblyName.GetAssemblyName(file);
            }
            catch (BadImageFormatException ex)
            {
                Logger.Warning(ex, "Bad image format of file {0}", file);
                
                return;
            }

            lock (results)
            {
                results.Add(new AssemblyLazy(assemblyName, file, isPlugin));
            }
        });

        return results;
    }

    /// <summary>
    ///     Validates and sorts the plugins based on dependencies.
    /// </summary>
    /// <param name="plugins">The plugin list to sort.</param>
    /// <returns>The plugins, sorted in dependency loading order.</returns>
    private static List<PluginInfo> CreateDependencyOrderedSet(IEnumerable<PluginInfo> plugins)
    {
        var pluginLookup = new Dictionary<string, PluginInfo>();
        var hardDependencies = new Dictionary<string, List<string>>();

        foreach (var plugin in plugins)
        {
            pluginLookup[plugin.Package] = plugin;

            hardDependencies[plugin.Package] = plugin
                .Dependencies
                .Where(p => p.Type == PluginDependencyAttribute.DependencyType.Required)
                .Select(p => p.PackageName)
                .ToList();
        }

        var installedPlugins = pluginLookup.Keys.ToList();

        // Check whether the Required Dependencies are present and remove those without.
        var validatedPlugins = ValidateHardDependencies(installedPlugins, hardDependencies);

        var dependencyGraph = validatedPlugins.ToDictionary(p => p, _ => new List<string>());

        foreach (var plugin in validatedPlugins)
        {
            foreach (var dependency in pluginLookup[plugin].Dependencies.Where(d => validatedPlugins.Contains(d.PackageName)))
            {
                // Optional dependencies can go two ways.
                // Also, how about cycles?

                if (dependency.Type == PluginDependencyAttribute.DependencyType.Optional)
                {
                    dependencyGraph[dependency.PackageName].Add(plugin);
                }
                else
                {
                    dependencyGraph[plugin].Add(dependency.PackageName);
                }
            }
        }

        var processed = new List<string>();

        var ordered = new List<PluginInfo>();

        foreach (var plugin in validatedPlugins.Where(plugin => !processed.Contains(plugin)))
        {
            TraverseDependencyGraph(plugin, dependencyGraph, processed, ordered, pluginLookup);
        }

        return ordered;
    }

    /// <summary>
    ///     Validates hard dependencies and creates a set of plugins that have all requirements met.
    /// </summary>
    /// <param name="plugins">The list of plugins to validate.</param>
    /// <param name="hardDependencies">Hard dependencies for each plugin to load.</param>
    /// <returns>The set of plugins which have all hard dependencies fulfilled.</returns>
    private static List<string> ValidateHardDependencies(List<string> plugins, IReadOnlyDictionary<string, List<string>> hardDependencies)
    {
        foreach (var plugin in plugins)
        {
            if (!hardDependencies.TryGetValue(plugin, out var hardDependency))
            {
                continue;
            }

            foreach (var dependency in hardDependency.Where(dependency => !plugins.Contains(dependency)))
            {
                Logger.Error("The plugin {0} depends on {1} but it is not installed! {2} will not loaded.", plugin, dependency, plugin);

                // Remove the plugin from the plugins to load.
                plugins.Remove(plugin);

                // Since other plugins might have defined the removed plugin as a hard dependency a recheck is necessary.
                return ValidateHardDependencies(plugins, hardDependencies);
            }
        }

        return plugins;
    }

    /// <summary>
    ///     Traverses the dependency graph and accumulates assemblies in the order of loading.
    /// </summary>
    /// <param name="plugin">Plugin to traverse dependencies for.</param>
    /// <param name="dependencyGraph">The dependency graph. It must contain an entry for the specified plugin.</param>
    /// <param name="processed">List of traversed entries.</param>
    /// <param name="ordered">The target collection. Ordered items will be added here.</param>
    /// <param name="pluginDictionary">
    ///     Lookup of plugin to PluginInfo, to access the plugin info for adding.
    ///     It must contain the plugin being processed and all of its dependencies.
    /// </param>
    private static void TraverseDependencyGraph(
        string plugin,
        IReadOnlyDictionary<string, List<string>> dependencyGraph,
        ICollection<string> processed,
        ICollection<PluginInfo> ordered,
        IReadOnlyDictionary<string, PluginInfo> pluginDictionary)
    {
        processed.Add(plugin);

        foreach (var dependency in dependencyGraph[plugin].Where(dependency => !processed.Contains(dependency)))
        {
            TraverseDependencyGraph(dependency, dependencyGraph, processed, ordered, pluginDictionary);
        }

        ordered.Add(pluginDictionary[plugin]);
    }
}

public class PluginLoaderException(string message, Exception? innerException = null) : Exception(message, innerException);