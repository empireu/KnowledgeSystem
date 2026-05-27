using KnowledgeSystem;
using KnowledgeSystem.Agents.Telemetry;
using KnowledgeSystem.Discord.Conversation;
using KnowledgeSystem.PluginLoader;
using KnowledgeSystem.Retrieval.Persistent;
using KnowledgeSystem.Retrieval.Telemetry;
using KnowledgeSystem.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddRagServices(context.Configuration);
    })
    .UseSerilog((context, loggerConfiguration) =>
    {
        loggerConfiguration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console();
    })
    .ConfigureServices(services =>
    {
        services.AddDiscordGateway(options =>
        {
            options.Intents = GatewayIntents.GuildMessages | GatewayIntents.MessageContent | GatewayIntents.Guilds;
        });
       
        services.AddApplicationCommands();
        services.AddSingleton<IConversationManager>(sp => sp.GetRequiredService<ConversationManager>());
        services.AddSingleton<ConversationManager>();
        services.AddHostedService(sp => sp.GetRequiredService<ConversationManager>());
        services.AddGatewayHandler<ExternalMessageHandler>();
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton<ResponseTracker>();
        services.AddHostedService<ResponseTracker>(sp => sp.GetRequiredService<ResponseTracker>());
    })
    .ConfigureServices(services =>
    {
        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => 
            {
                resource.AddService("KnowledgeSystem");
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(RetrievalTelemetry.Retrieval.Name);
                tracing.AddSource(KnowledgeSystemTelemetry.AgentTools.Name);
                tracing.AddSource(AgentTelemetry.Agent.Name);
                tracing.AddSource(KnowledgeSystemTelemetry.AgentChat.Name);
                
                tracing.AddOtlpExporter(options => 
                {
                    options.Endpoint = new Uri("http://localhost:4317");
                    options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                });
            });
    })
    .UsePluginLoader(PluginLoader.PluginLoaderOptions.Default);

var host = builder.Build();

var engine = host.Services.GetRequiredService<DiskWikiStore>();
await engine.InitializeAsync();
engine.Warmup();

host.AddApplicationCommandModule<MqrModule>();

await host.RunAsync();