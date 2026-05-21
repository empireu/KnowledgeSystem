using System.Diagnostics;

namespace KnowledgeSystems.Extensions;

public static class TelemetryExtensions
{
    public static Activity? StartActivity(this ActivitySource source, string name, ActivityKind kind)
    {
       return source.StartActivity(name, kind);
    }
    
    public static Activity? StartInternalActivity(this ActivitySource source, string name)
    {
        // ReSharper disable once RedundantArgumentDefaultValue
        return source.StartActivity(name);
    }
}