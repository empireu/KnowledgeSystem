using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;

namespace KnowledgeSystem.Agents.Orchestration;

/// <summary>
///     Represents a frame in the running tool list.
///     The tool could be a simple tool or an agent.
///     The frame can also represent an LLM error (i.e. hallucinated tool or an argument error), so there is no running task attached.
/// </summary>
public abstract class AgentToolFrame
{
    /// <summary>
    ///     The API call ID (<b>not the tool name</b>), used to route tool results back to the LLM.
    /// </summary>
    private AgentToolFrame(string callId)
    {
        CallId = callId;
    }

    /// <summary>
    ///     The API call ID, used for <see cref="AgentExecutionContext.InsertToolResult"/>.
    /// </summary>
    public string CallId { get; }

    /// <summary>
    ///     Represents a hallucinated tool.
    /// </summary>
    public sealed class Hallucination(string callId, string functionName, string errorMessage) : AgentToolFrame(callId)
    {
        /// <summary>
        ///     The hallucinated function name the LLM tried to call.
        /// </summary>
        public string FunctionName { get; } = functionName;

        /// <summary>
        ///     The message that gets reported to the LLM.
        /// </summary>
        public string ErrorMessage { get; } = errorMessage;
    }

    /// <summary>
    ///     Represents a tool call that is missing some required arguments.
    /// </summary>
    public sealed class MissingArgs(string callId, string errorMessage) : AgentToolFrame(callId)
    {
        /// <summary>
        ///     The message that gets reported to the LLM.
        /// </summary>
        public string ErrorMessage { get; } = errorMessage;
    }

    /// <summary>
    ///     Interface for a resolved tool call.
    /// </summary>
    public abstract class RunningFrame : AgentToolFrame
    {
        internal RunningFrame(string callId, AgentTool tool, ArgumentExtractionResult args) : base(callId)
        {
            Tool = tool;
            Args = args;
        }

        /// <summary>
        ///     The tool being called.
        /// </summary>
        public AgentTool Tool { get; }

        /// <summary>
        ///     The arguments for the call.
        /// </summary>
        public ArgumentExtractionResult Args { get; }
        
        /// <summary>
        ///     The final result. Always non-null when the tool finished.
        /// </summary>
        public ToolExecutionResult? Result { get; internal set; }
    }
    
    /// <summary>
    ///     Represents a tool call that resolved to a registered tool successfully. This doesn't invoke any sub-agents and is simply a routine that will execute.
    /// </summary>
    public sealed class PlainRunningFrame(string callId, AgentTool tool, ArgumentExtractionResult args) : RunningFrame(callId, tool, args)
    {
        /// <summary>
        ///     The running task. Started and set on the next turn, after the frame is pushed.
        /// </summary>
        public Task<ToolExecutionResult>? StartedTask { get; internal set; }
    }
    
    /// <summary>
    ///     Tool call that runs a sub-agent.
    /// </summary>
    public sealed class RunningSubAgent(string callId, AgentTool tool, ArgumentExtractionResult args) : RunningFrame(callId, tool, args)
    {
        /// <summary>
        ///     The proxy holding the runner and the stepping routine. Set on the next turn.
        /// </summary>
        public ISubAgentProxy? Proxy { get; internal set; }
    }
}