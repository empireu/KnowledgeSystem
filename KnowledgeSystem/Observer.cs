using KnowledgeSystem.Agents.Context.TokenEstimation;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Observer;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using OpenAI.Chat;

namespace KnowledgeSystem;

public sealed class Observer(ITokenEstimator tokenEstimator) : IAgentObserver
{
    public Task OnToolCallAsync(AgentRunner runner, ToolCallInfo info, CancellationToken cancellationToken)
    {
        var arguments = string.Join(", ", info.Args.Arguments.Select(kvp => $"{kvp.Key.ArgumentName}=\"{kvp.Value}\""));
        Console.WriteLine($"({runner.Agent.AgentId}) [tool] {info.Tool.ToolId}({arguments})");
        return Task.CompletedTask;   
    }

    public Task OnToolResultAsync(AgentRunner runner, int indexInCollection, AgentTool tool, ToolExecutionResult result, CancellationToken cancellationToken)
    {
        var message = new ToolChatMessage(result.Tool.ToolId, result.ToString());
        var tokens = tokenEstimator.CountTokens([message]);
        Console.WriteLine($"({runner.Agent.AgentId}) [tool {indexInCollection} result for {tool.ToolId}, {(result.IsSuccessful ? "OK" : "ERR")}] {result.Tool.ToolId}: +{tokens} tokens");
        return Task.CompletedTask;
    }

    public Task OnAssistantMessageAsync(AgentRunner runner, string message, CancellationToken cancellationToken)
    {
        Console.WriteLine($"({runner.Agent.AgentId}) [assistant] {message}");
        return Task.CompletedTask;
    }

    public Task OnAgentCompletedAsync(AgentRunner runner, CancellationToken cancellationToken)
    {
        Console.WriteLine(runner.FinishError == null
            ? $"({runner.Agent.AgentId}) completed successfully"
            : $"({runner.Agent.AgentId}) completed with error: {runner.FinishError}"
        );
        
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(AgentRunner runner, AgentExecutionError error, CancellationToken cancellationToken)
    {
        Console.WriteLine($"({runner.Agent.AgentId}) {(error.IsCritical ? "CRITICAL ERROR" : "error")}: {error}");
        return Task.CompletedTask;   
    }
}
