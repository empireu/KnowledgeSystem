using Microsoft.Extensions.Logging;
using NetCord.Services.ApplicationCommands;

namespace KnowledgeSystem.Discord;

public class TestModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ILogger<TestModule> _logger;

    public TestModule(ILogger<TestModule> logger)
    {
        _logger = logger;
    }
    
    [SlashCommand("test", "Tests the command")]
    public string Respond([SlashCommandParameter] string prompt)
    {
        _logger.LogInformation("Called with {message}", prompt);
        return $"Pong: {prompt}";
    }
}