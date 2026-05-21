using System.Diagnostics;

namespace KnowledgeSystem.Retrieval.Telemetry;

public static class RagTelemetry
{
    public static readonly ActivitySource Rag = new("Rag");
}