using KnowledgeSystem.PluginLoader;
using KnowledgeSystem.Plugins.CodeMemory.Memory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Plugins.CodeMemory;

[Plugin("CodeMemory", "mqr.code.memory", "0.0.0", "empireu")]
public class CodeMemoryPlugin(
    ILogger<CodeMemoryPlugin> logger,
    MemoryStoreService memoryStoreService,
    MemoryExtractionService memoryExtractionService,
    IOptions<MemorySystemConfig> config,
    ILoggerFactory loggerFactory,
    RecallSystem recallSystem
) : IPlugin
{
    private WebApplication? _app;
    
    public async Task Start(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = "CodeMemory-MCP"
        });

        builder.WebHost.UseUrls(config.Value.ServerUrl);

        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(loggerFactory);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

        // Shared services:
        builder.Services.AddSingleton<MemoryStoreService>(_ => memoryStoreService);
        builder.Services.AddSingleton<MemoryExtractionService>(_ => memoryExtractionService);
        builder.Services.AddSingleton<RecallSystem>(_ => recallSystem);

        // MCP:
        builder.Services.AddMcpServer()
            .WithHttpTransport(o => o.Stateless = false)
            .WithTools<CodeMemoryTools>();
        
        _app = builder.Build();

        var apiKey = config.Value.ApiKey;
        if (!string.IsNullOrEmpty(apiKey))
        {
            _app.Use(async (context, next) =>
            {
                string? token = null;

                var auth = context.Request.Headers.Authorization.FirstOrDefault();
                if (auth != null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = auth["Bearer ".Length..];
                }

                token ??= context.Request.Query["token"].FirstOrDefault();

                if (token == apiKey)
                {
                    await next();
                    return;
                }

                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized", cancellationToken: cancellationToken);
            });
        }

        _app.MapMcp();
        
        await _app.StartAsync(cancellationToken);
        
        logger.LogInformation("CodeMemory MCP server listening on {url}", config.Value.ServerUrl);
    }

    public async Task Stop(CancellationToken cancellationToken)
    {
        if (_app != null)
        {
            await _app.StopAsync(cancellationToken);
            await _app.DisposeAsync();
            _app = null;
            logger.LogInformation("CodeMemory MCP server stopped");
        }
    }
}