using KnowledgeSystem.Agents.Telemetry;
using KnowledgeSystem.Events.Implementation;
using KnowledgeSystems.Extensions;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Plugins.Library;

public class AgentEventManager(ILogger<DefaultEventManager>? errorLogger, IServiceProvider serviceProvider) : DefaultEventManager(errorLogger, serviceProvider)
{
    public override ValueTask SendAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        using var activity = AgentTelemetry.Agent.StartInternalActivity("DispatchEvent");
        
        activity?.SetTag("event_type", @event.GetType().Name);
        
        return base.SendAsync(@event, cancellationToken);
    }
}