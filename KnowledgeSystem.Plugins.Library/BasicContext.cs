using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Orchestration;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Plugins.Library;

public class BasicContext : AgentExecutionContext
{
    public AgentContext ChatContext { get; } = new();

    public override IReadOnlyList<ChatMessage> ChatMessages => ChatContext.ChatMessages;

    public override void InsertAssistantCompletion(ChatResponse response)
    {
        ChatContext.InsertAssistant(response);
    }

    public override void InsertToolResult(string toolCallId, string output)
    {
        var contents = new List<AIContent>
        {
            new FunctionResultContent(toolCallId, output)
        };
        
        ChatContext.InsertChat(new ChatMessage(ChatRole.Tool, contents));
    }
}
