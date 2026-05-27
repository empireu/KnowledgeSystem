using KnowledgeSystem;
using KnowledgeSystem.PluginLoader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord.Hosting.Services.ApplicationCommands;
using Serilog;

var builder = Host.CreateDefaultBuilder(args)
    .UseSerilog((context, loggerConfiguration) =>
    {
        loggerConfiguration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console();
    })
    .ConfigureServices(services =>
    {
        services.AddOptions<KnowledgeSystemConfig>()
            .BindConfiguration(KnowledgeSystemConfig.Section)
            .ValidateOnStart();
    })
    .WithCoreServices()
    .UsePluginLoader(PluginLoader.PluginLoaderOptions.Default)
    .WithDiscordIntegration()
    .UseApplicationCommands()
    .WithTelemetryServices();

var host = builder.Build();

await host.RunAsync();