using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Retrieval.Api.Graph;

namespace KnowledgeSystem.Retrieval.Graph.Extraction;

public sealed class RecordEntityToolHandler(
    AgentTool tool,
    StringArgument primaryNameArgument,
    ArrayArgument aliasesArgument,
    StringArgument typeArgument,
    StringArgument descriptionArgument,
    StringArgument evidenceArgument
) : ToolHandler<ExtractionContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<ExtractionContext> registry)
    {
        var recordEntityTool = new ToolBuilder("record_entity")
            .WithDescription(
                "Records a named entity. Evidence must be an exact source substring. Call in parallel for all entities.")
            .WithRequiredStringArgument("primary_name", "Primary name of the entity.", out var primaryName)
            .WithArrayArgument("aliases", "Alternative names/aliases. Can be empty.", out var aliases)
            .WithStringArgument("type", "Free-form type (e.g. 'person', 'organization', 'ship').", out var type)
            .WithStringArgument("description", "Short description.", out var description)
            .WithRequiredStringArgument("evidence", "Exact source substring identifying this entity.", out var evidence)
            .Build();

        var handler = new RecordEntityToolHandler(
            recordEntityTool,
            primaryName,
            aliases,
            type,
            description,
            evidence
        );

        registry.RegisterTool(recordEntityTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<ExtractionContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var context = runner.ExecutionContext;
        var primaryName = primaryNameArgument.GetValue(args);
        var aliases = aliasesArgument.TryGetValue(args, out var aliasValues) ? aliasValues : [];
        var type = typeArgument.GetValueOrNull(args);
        var description = descriptionArgument.GetValueOrNull(args);
        var evidenceText = evidenceArgument.GetValue(args);

        // Match evidence against source (exact, then normalized fallback):
        var matchResult = EvidenceMatcher.TryMatch(context.SourceContent, evidenceText);
        if (!matchResult.IsSuccess)
        {
            var diagnostic = matchResult.FailureDiagnostic ?? "Use exact source substring.";
            return Task.FromResult(Error($"Evidence not found. {diagnostic}"));
        }

        var names = aliases is { Length: > 0 }
            ? aliases.Prepend(primaryName).ToArray()
            : [primaryName];
        var entity = new RawExtractedEntity(matchResult.Evidence!, names, description, type);

        context.RecordedEntities[primaryName] = entity;

        var aliasList = aliases is { Length: > 0 } ? $" aka {string.Join(", ", aliases)}" : "";
        return Task.FromResult(Success($"OK: {primaryName}{aliasList}"));
    }
}
