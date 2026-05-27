using System.Diagnostics;

// ReSharper disable MemberCanBePrivate.Global

namespace KnowledgeSystem.Api;

// TODO temporary measure
public static class KnowledgeSystemTelemetry
{
    public static readonly ActivitySource AgentTools = new("Agent.Tools");
    public static readonly ActivitySource AgentChat = new("Agent.Chat");
}