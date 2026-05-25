using System.Diagnostics;

namespace KnowledgeSystem.Retrieval.Telemetry;

public static class RetrievalTelemetry
{
    public static readonly ActivitySource Retrieval = new("Retrieval");
}