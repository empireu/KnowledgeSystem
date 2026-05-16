using KnowledgeSystem.Discord;
using KnowledgeSystem.Discord.Conversation;
using KnowledgeSystem.Retrieval;
using KnowledgeSystem.Retrieval.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;
using Serilog;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddRagServices(context.Configuration);

        services.AddOptions<ChatOptions>()
            .BindConfiguration(ChatOptions.Section)
            .ValidateOnStart();
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
        services.AddSingleton<IConversationManager, ConversationManager>();
        services.AddGatewayHandler<MessageHandler>();
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton<DiscordObserverFactory>();
    });

var host = builder.Build();

var engine = host.Services.GetRequiredService<RagEngine>();
await engine.InitializeAsync();

host.AddApplicationCommandModule<MqrModule>();

await host.RunAsync();