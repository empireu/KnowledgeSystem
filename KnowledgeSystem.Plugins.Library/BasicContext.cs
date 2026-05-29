using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Orchestration;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Plugins.Library;

public class BasicContext : AgentExecutionContext
{
    public AgentContext Timeline { get; } = new();

    public override IReadOnlyList<ChatMessage> ChatMessages => Timeline.ChatMessages;

    public override void InsertAssistantCompletion(ChatResponse response)
    {
        Timeline.InsertAssistant(response);
    }

    public override void InsertToolResult(string toolCallId, string output)
    {
        var contents = new List<AIContent>
        {
            new FunctionResultContent(toolCallId, output)
        };
        
        Timeline.InsertChat(new ChatMessage(ChatRole.Tool, contents));
    }
}
