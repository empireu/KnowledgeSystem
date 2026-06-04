using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.RunnerEvents;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Events.Api;

// ReSharper disable UnusedMember.Global

namespace KnowledgeSystem.Retrieval.Graph.Extraction;

public sealed class ConsoleExtractionObserver : IEventReceiver
{
    private int _roundNumber;

    [SubscribeEvent(IsCritical = false)]
    public ValueTask OnToolCallsAsync(AgentToolCallsEvent @event, CancellationToken cancellationToken)
    {
        _roundNumber++;
        Console.WriteLine($"  ── Round {_roundNumber} ──");

        if (!string.IsNullOrWhiteSpace(@event.Response.Text))
        {
            var text = @event.Response.Text.Replace('\n', ' ').Replace('\r', ' ');
            if (text.Length > 120)
            {
                text = text[..117] + "...";
            }

            Console.WriteLine($"  LLM: \"{text}\"");
        }

        foreach (var call in @event.Calls)
        {
            if (!call.IsValid)
            {
                continue;
            }

            var args = FormatArguments(call.Args);
            Console.WriteLine($"  → {call.Tool.ToolId}({args})");
        }

        return ValueTask.CompletedTask;
    }

    [SubscribeEvent(IsCritical = false)]
    public ValueTask OnToolResultAsync(AgentToolResultEvent @event, CancellationToken cancellationToken)
    {
        var icon = @event.Result.IsSuccessful ? "✓" : "✗";
        var toolId = @event.Tool.ToolId;

        if (@event.Result.IsSuccessful)
        {
            var output = @event.Result.Output;
            if (string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine($"  {icon} {toolId}");
            }
            else
            {
                var text = output.Replace('\n', ' ').Replace('\r', ' ');
                if (text.Length > 100)
                {
                    text = text[..97] + "...";
                }

                Console.WriteLine($"  {icon} {toolId}: {text}");
            }
        }
        else
        {
            var error = @event.Result.FormatError();
            var text = error.Replace('\n', ' ').Replace('\r', ' ');
            if (text.Length > 100)
            {
                text = text[..97] + "...";
            }

            Console.WriteLine($"  {icon} {toolId}: {text}");
        }

        return ValueTask.CompletedTask;
    }

    [SubscribeEvent(IsCritical = false)]
    public ValueTask OnTurnAsync(AgentTurnEvent @event, CancellationToken cancellationToken)
    {
        if (@event.TurnStatus == AgentRunner.TurnStatus.CompletedWithError)
        {
            Console.WriteLine("  ⚠ Turn completed with error");
        }

        return ValueTask.CompletedTask;
    }

    [SubscribeEvent(IsCritical = false)]
    public ValueTask OnErrorAsync(AgentErrorEvent @event, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  ✗ Error: {@event.Error.Message}");
        return ValueTask.CompletedTask;
    }

    [SubscribeEvent(IsCritical = false)]
    public ValueTask OnCompletedAsync(AgentCompletedEvent @event, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  Done ({_roundNumber} rounds)");
        return ValueTask.CompletedTask;
    }

    private static string FormatArguments(ArgumentExtractionResult arguments)
    {
        var parts = new List<string>();

        foreach (var (argument, value) in arguments.Arguments)
        {
            var stringValue = ChatMessageHelpers.FormatContentValue(value) ?? "null";

            if (stringValue.Length > 50)
            {
                stringValue = stringValue[..47] + "...";
            }

            stringValue = stringValue.Replace('\n', ' ').Replace('\r', ' ');

            if (argument is StringArgument)
            {
                stringValue = $"\"{stringValue}\"";
            }

            parts.Add($"{argument.ArgumentName}: {stringValue}");
        }

        return string.Join(", ", parts);
    }
}
