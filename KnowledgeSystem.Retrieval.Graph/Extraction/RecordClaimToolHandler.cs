using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Retrieval.Api.Graph;

namespace KnowledgeSystem.Retrieval.Graph.Extraction;

public sealed class RecordClaimToolHandler(
    AgentTool tool,
    StringArgument subjectArgument,
    StringArgument predicateArgument,
    StringArgument objectEntityArgument,
    StringArgument objectLiteralArgument,
    EnumArgument modalityArgument,
    StringArgument evidenceArgument
) : ToolHandler<ExtractionContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<ExtractionContext> registry)
    {
        var modalities = new[]
        {
            // Epistemic:
            "fact",
            "opinion",
            "speculation",
            "negation",
            // Speech acts:
            "command",
            "question",
            "joke",
            // Internal states:
            "emotion",
            "desire",
            "intention",
            "sensation",
            // Behavioral:
            "action",
            // Deontic:
            "obligation",
            "permission",
            "ability",
            // Relational / structural:
            "state",
            "possession",
            "identity",
            "causation",
            "attribution",
            "comparison"
        };

        var recordClaimTool = new ToolBuilder("record_claim")
            .WithDescription(
                "Records a claim about entities. Subject must be a recorded entity. " +
                "Use object_entity for named entities (must be recorded), object_literal for values/phrases. Exactly one required. " +
                "Evidence must be an exact source substring. Call in parallel for all claims.")
            .WithRequiredStringArgument("subject", "Primary name of subject entity (must be recorded).", out var subject)
            .WithRequiredStringArgument("predicate", "Semantic relationship, NOT 'said' (e.g. 'invented', 'is', 'ordered').", out var predicate)
            .WithStringArgument("object_entity", "Name of object entity (must be recorded).", out var objectEntity)
            .WithStringArgument("object_literal", "Literal value, phrase, or description.", out var objectLiteral)
            .WithRequiredEnumArgument("modality", "Claim modality.", modalities, out var modality)
            .WithRequiredStringArgument("evidence", "Exact source substring.", out var evidence)
            .Build();

        var handler = new RecordClaimToolHandler(
            recordClaimTool,
            subject,
            predicate,
            objectEntity,
            objectLiteral,
            modality,
            evidence
        );

        registry.RegisterTool(recordClaimTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<ExtractionContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var context = runner.ExecutionContext;
        var subjectName = subjectArgument.GetValue(args);
        var predicate = predicateArgument.GetValue(args);
        var objectEntityName = NormalizeNullString(objectEntityArgument.GetValueOrNull(args));
        var objectLiteralValue = NormalizeNullString(objectLiteralArgument.GetValueOrNull(args));
        var modality = modalityArgument.GetValue(args);
        var evidenceText = evidenceArgument.GetValue(args);

        // Resolve subject:
        if (!context.RecordedEntities.TryGetValue(subjectName, out var subjectEntity))
        {
            return Task.FromResult(Error($"Unknown subject '{subjectName}'. Record it first."));
        }

        RawExtractedEntity? objectEntity = null;
        string? objectLiteral = null;

        if (!string.IsNullOrEmpty(objectEntityName) && !string.IsNullOrEmpty(objectLiteralValue))
        {
            // Both provided.
            // Prefer entity if it resolves, else fallback to literal:
            if (context.RecordedEntities.TryGetValue(objectEntityName, out var resolvedEntity))
            {
                objectEntity = resolvedEntity;
                objectLiteral = null;
            }
            else
            {
                objectLiteral = objectLiteralValue;
                objectEntity = null;
            }
        }
        else if (!string.IsNullOrEmpty(objectEntityName))
        {
            if (!context.RecordedEntities.TryGetValue(objectEntityName, out var resolvedEntity))
            {
                // Fallback unresolved object_entity to literal
                objectLiteral = objectEntityName;
                objectEntity = null;
            }
            else
            {
                objectEntity = resolvedEntity;
            }
        }
        else if (!string.IsNullOrEmpty(objectLiteralValue))
        {
            objectLiteral = objectLiteralValue;
        }
        else
        {
            return Task.FromResult(Error("Provide exactly one of object_entity or object_literal."));
        }

        // Match evidence against source (exact, then normalized fallback):
        var matchResult = EvidenceMatcher.TryMatch(context.SourceContent, evidenceText);
        if (!matchResult.IsSuccess)
        {
            var diagnostic = matchResult.FailureDiagnostic ?? "Use exact source substring.";
            return Task.FromResult(Error($"Evidence not found. {diagnostic}"));
        }

        var claim = new RawExtractedClaim(subjectEntity, predicate, objectEntity, objectLiteral, modality, matchResult.Evidence!);
        context.RecordedClaims.Add(claim);

        var objectDesc = objectEntity != null 
            ? $"[{objectEntity.DefinedNames[0]}]" 
            : $"\"{objectLiteral}\"";
        
        return Task.FromResult(Success($"OK: [{subjectName}] {predicate} {objectDesc} ({modality})"));
    }

    private static string? NormalizeNullString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value;
    }
}
