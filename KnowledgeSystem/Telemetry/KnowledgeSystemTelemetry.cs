using System.Diagnostics;

// ReSharper disable MemberCanBePrivate.Global

namespace KnowledgeSystem.Telemetry;

public static class KnowledgeSystemTelemetry
{
    internal static readonly ActivitySource AgentTools = new("Agent.Tools");
    internal static readonly ActivitySource AgentChat = new("Agent.Chat");
}