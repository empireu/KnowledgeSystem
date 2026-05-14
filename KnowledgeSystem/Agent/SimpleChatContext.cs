using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Orchestration;
using OpenAI.Chat;

namespace KnowledgeSystem.Agent;

public sealed class SimpleChatContext : AgentExecutionContext
{
    public AgentContext ChatContext { get; } = new();

    public override IReadOnlyList<ChatMessage> ChatMessages => ChatContext.ChatMessages;

    public override void InsertAssistantCompletion(ChatCompletion completion)
    {
        ChatContext.InsertAssistant(completion);
    }

    public override void InsertToolResult(string toolCallId, string output)
    {
        ChatContext.InsertChat(new ToolChatMessage(toolCallId, output));
    }
}
