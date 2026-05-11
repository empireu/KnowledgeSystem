using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using OpenAI.Chat;

namespace KnowledgeSystem.Agents.Tools;

public sealed class ArgumentExtractionResult
{
    public enum ExtractionStatus
    {
        /// <summary>
        ///     All arguments were matched.
        /// </summary>
        Success,
        /// <summary>
        ///     The required arguments were not matched.
        /// </summary>
        IncompleteArguments
    }
    
    /// <summary>
    ///     The status of the argument extraction.
    /// </summary>
    public required ExtractionStatus Status { get; init; }
    
    /// <summary>
    ///     The data extracted for each argument.
    /// </summary>
    public required Dictionary<ToolArgument, string?> Arguments { get; init; }
    
    /// <summary>
    ///     The <b>required</b> arguments missing from the data.
    /// </summary>
    public required IReadOnlyList<ToolArgument> MissingArguments { get; init; }
}

public sealed class ToolSet
{
    public readonly Dictionary<string, ToolDefinition> Tools = [];

    /// <summary>
    ///     Adds a tool to the set.
    /// </summary>
    public void AddTool(ToolDefinition toolDefinition)
    {
        if (!Tools.TryAdd(toolDefinition.ToolId, toolDefinition))
        {
            throw new InvalidOperationException($"Duplicate tool definition {toolDefinition.Tool}");
        }
    }

    /// <summary>
    ///     Tries to match a tool from the model's response.
    /// </summary>
    /// <param name="toolCall">The raw tool call, returned by the SDK.</param>
    /// <param name="tool">If true, the tool that was matched.</param>
    /// <returns>True if a tool was matched. Otherwise, false.</returns>
    public bool TryMatchTool(ChatToolCall toolCall, [NotNullWhen(true)] out ToolDefinition? tool)
    {
        return Tools.TryGetValue(toolCall.FunctionName, out tool);
    }

    /// <summary>
    ///     Extracts the arguments from a tool call.
    /// </summary>
    /// <param name="tool">The tool to extract arguments from.</param>
    /// <param name="functionArguments">The raw argument data.</param>
    /// <returns></returns>
    public static ArgumentExtractionResult ExtractArguments(ToolDefinition tool, BinaryData functionArguments)
    {
        using var jsonDoc = JsonDocument.Parse(functionArguments);
        var root = jsonDoc.RootElement;

        var arguments = new Dictionary<ToolArgument, string?>();
        var missing = new List<ToolArgument>();

        foreach (var arg in tool.Arguments)
        {
            if (root.TryGetProperty(arg.ArgumentName, out var valueElement))
            {
                arguments[arg] = valueElement.ValueKind == JsonValueKind.String
                    ? valueElement.GetString()
                    : valueElement.GetRawText();
            }
            else
            {
                arguments[arg] = null;
            }
        }

        foreach (var requiredArg in tool.RequiredArguments)
        {
            if (arguments[requiredArg] == null)
            {
                missing.Add(requiredArg);
            }
        }

        return new ArgumentExtractionResult
        {
            Status = missing.Count == 0 
                ? ArgumentExtractionResult.ExtractionStatus.Success
                : ArgumentExtractionResult.ExtractionStatus.IncompleteArguments,
            Arguments = arguments,
            MissingArguments = missing
        };
    }
}