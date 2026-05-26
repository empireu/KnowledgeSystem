using System.Diagnostics;

// ReSharper disable MemberCanBePrivate.Global

namespace KnowledgeSystem.Telemetry;

// TODO temporary measure
public static class KnowledgeSystemTelemetry
{
    public static readonly ActivitySource AgentTools = new("Agent.Tools");
    public static readonly ActivitySource AgentChat = new("Agent.Chat");
}