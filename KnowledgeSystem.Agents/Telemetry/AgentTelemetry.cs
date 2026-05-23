using System.Diagnostics;

namespace KnowledgeSystem.Agents.Telemetry;

public static class AgentTelemetry
{
    public static readonly ActivitySource Agent = new("Agent");
}