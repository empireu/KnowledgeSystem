using KnowledgeSystem.PluginLoader;
using KnowledgeSystem.Plugins.CodeMemory.Memory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Plugins.CodeMemory;

[Plugin("CodeMemory", "mqr.code.memory", "0.0.0", "empireu")]
public class CodeMemoryPlugin(
    ILogger<CodeMemoryPlugin> logger,
    MemoryStoreService memoryStoreService,
    MemoryExtractionService memoryExtractionService,
    IOptions<MemorySystemConfig> config
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

        // Shared services:
        builder.Services.AddSingleton<MemoryStoreService>(_ => memoryStoreService);
        builder.Services.AddSingleton<MemoryExtractionService>(_ => memoryExtractionService);

        // MCP:
        builder.Services.AddMcpServer()
            .WithHttpTransport(o => o.Stateless = false)
            .WithTools<CodeMemoryTools>();
        
        _app = builder.Build();
        _app.MapMcp();
        
        await _app.StartAsync(cancellationToken);
        
        logger.LogInformation("CodeMemory MCP server listening on {url}", config.Value.ServerUrl);
    }
}