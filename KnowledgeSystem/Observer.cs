using System.Text;
using KnowledgeSystem.Agents.Context.TokenEstimation;
using KnowledgeSystem.Agents.Orchestration.Observer;
using OpenAI.Chat;

namespace KnowledgeSystem;

public sealed class Observer(ITokenEstimator tokenEstimator) : IAgentObserver
{
    public Task OnToolCallAsync(ToolCallInfo[] toolCalls, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[tool calls]");
        
        foreach (var toolCallInfo in toolCalls)
        {
            var arguments = string.Join(", ", toolCallInfo.Args.Arguments.Select(kvp => $"{kvp.Key.ArgumentName}=\"{kvp.Value}\""));
            
            sb.AppendLine($"  {toolCallInfo.Tool.ToolId}({arguments})");
        }

        Console.WriteLine(sb);
     
        return Task.CompletedTask;
    }

    public Task OnToolResultAsync(ToolCallResult result, CancellationToken cancellationToken)
    {
        var message = new ToolChatMessage(result.Tool.ToolId, result.Content);
        var tokens = tokenEstimator.CountTokens([message]);
        
        Console.WriteLine($"[tool result {result.IndexInCollection}] {result.Tool.ToolId}: +{tokens} tokens");
        return Task.CompletedTask;
    }

    public Task OnAssistantMessageAsync(string message, CancellationToken cancellationToken)
    {
        Console.WriteLine(message);
        return Task.CompletedTask;
    }

    public Task OnAgentCompletedAsync(AgentExecutionResult result, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[done] {result.FinalStatus}");
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(AgentExecutionError error, CancellationToken cancellationToken)
    {
        Console.WriteLine(error.IsCritical ? $"[CRITICAL ERROR] {error.Message}" : $"[error] {error.Message}");
        return Task.CompletedTask;
    }
}
