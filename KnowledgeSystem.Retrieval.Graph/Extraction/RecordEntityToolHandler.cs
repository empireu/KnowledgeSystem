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
                "Records a named entity extracted from the text. " +
                "The evidence MUST be an exact, character-for-character substring of the source text. " +
                "Call this tool in parallel for all entities you find.")
            .WithRequiredStringArgument("primary_name", "The primary name of the entity (e.g. 'Jim Holden', 'OPA', 'Canterbury').", out var primaryName)
            .WithArrayArgument("aliases", "Alternative names or aliases used in the text (e.g. ['Holden', 'XO', 'Cant']). Can be empty.", out var aliases)
            .WithStringArgument("type", "Free-form entity type (e.g. 'person', 'organization', 'ship', 'location').", out var type)
            .WithStringArgument("description", "Short description of the entity specific to this text.", out var description)
            .WithRequiredStringArgument("evidence", "EXACT quoted text from the source that identifies this entity. Must be a verbatim substring.", out var evidence)
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

        // Exact-match evidence against source:
        var index = context.SourceContent.IndexOf(evidenceText, StringComparison.Ordinal);
        if (index < 0)
        {
            return Task.FromResult(Error(
                $"Evidence text not found verbatim in source. Make sure the 'evidence' argument is an exact, character-for-character substring of the source text. " +
                $"Check for whitespace, punctuation, and formatting differences. The closest partial match may help you find the correct quote."
            ));
        }

        var span = new TextSpan(index, evidenceText.Length);

        var names = aliases is { Length: > 0 }
            ? aliases.Prepend(primaryName).ToArray()
            : [primaryName];

        var evidence = new RawEvidence(evidenceText, context.SourceContent, span);
        var entity = new RawExtractedEntity(evidence, names, description, type);

        context.RecordedEntities[primaryName] = entity;

        var aliasList = aliases is { Length: > 0 } ? $" (aliases: {string.Join(", ", aliases)})" : "";
        return Task.FromResult(Success($"Recorded entity: {primaryName}{aliasList}"));
    }
}
