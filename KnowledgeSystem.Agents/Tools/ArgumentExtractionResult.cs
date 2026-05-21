using System.Text.Json;

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
    public required Dictionary<ToolArgument, object?> Arguments { get; init; }
    
    /// <summary>
    ///     The <b>required</b> arguments missing from the data.
    /// </summary>
    public required IReadOnlyList<ToolArgument> MissingArguments { get; init; }
    
    /// <summary>
    ///     Extracts the arguments from a tool call.
    /// </summary>
    /// <param name="agentTool">The tool to extract arguments from.</param>
    /// <param name="functionArguments">The arguments dictionary from <see cref="Microsoft.Extensions.AI.FunctionCallContent.Arguments"/>.</param>
    /// <returns></returns>
    public static ArgumentExtractionResult ExtractArguments(AgentTool agentTool, IDictionary<string, object?>? functionArguments)
    {
        var arguments = new Dictionary<ToolArgument, object?>();
        var missing = new List<ToolArgument>();

        foreach (var arg in agentTool.Arguments)
        {
            if (functionArguments != null && functionArguments.TryGetValue(arg.ArgumentName, out var value) && value != null)
            {
                arguments[arg] = value is JsonElement jsonElement
                    ? jsonElement.Clone()
                    : value;
            }
            else
            {
                arguments[arg] = null;
            }
        }

        foreach (var requiredArg in agentTool.RequiredArguments)
        {
            if (arguments[requiredArg] == null)
            {
                missing.Add(requiredArg);
            }
        }

        return new ArgumentExtractionResult
        {
            Status = missing.Count == 0 
                ? ExtractionStatus.Success
                : ExtractionStatus.IncompleteArguments,
            Arguments = arguments,
            MissingArguments = missing
        };
    }
}
