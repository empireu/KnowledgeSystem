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
            "fact",
            "opinion",
            "speculation",
            "negation",
            "command",
            "question",
            "joke"
        };

        var recordClaimTool = new ToolBuilder("record_claim")
            .WithDescription(
                "Records a claim (a statement about entities) extracted from the text. " +
                "The subject MUST be the primary name of an entity already recorded via record_entity. " +
                "Use object_entity when the object is a named entity (must already be recorded); use object_literal for values, phrases, or descriptions. " +
                "Exactly one of object_entity and object_literal must be provided. " +
                "The evidence MUST be an exact, character-for-character substring of the source text. " +
                "Call this tool in parallel for all claims you find.")
            .WithRequiredStringArgument("subject", "Primary name of the subject entity (must match a previously recorded entity).", out var subject)
            .WithRequiredStringArgument("predicate", "The relationship or action (e.g. 'invented', 'is', 'ordered', 'works for'). Use a semantic predicate, NOT 'said'.", out var predicate)
            .WithStringArgument("object_entity", "Primary name of the object entity, if the object is a named entity. Must match a recorded entity.", out var objectEntity)
            .WithStringArgument("object_literal", "A literal value, phrase, or description as the object, if not a named entity.", out var objectLiteral)
            .WithRequiredEnumArgument("modality", "The modality of the claim.", modalities, out var modality)
            .WithRequiredStringArgument("evidence", "EXACT quoted text from the source that supports this claim. Must be a verbatim substring.", out var evidence)
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
        var objectEntityName = objectEntityArgument.GetValueOrNull(args);
        var objectLiteralValue = objectLiteralArgument.GetValueOrNull(args);
        var modality = modalityArgument.GetValue(args);
        var evidenceText = evidenceArgument.GetValue(args);

        // Resolve subject:
        if (!context.RecordedEntities.TryGetValue(subjectName, out var subjectEntity))
        {
            return Task.FromResult(Error($"Subject '{subjectName}' is not a recorded entity. Record it with record_entity first."));
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
            return Task.FromResult(Error("Exactly one of object_entity or object_literal must be provided."));
        }

        // Exact-match evidence against source:
        var evidenceIndex = context.SourceContent.IndexOf(evidenceText, StringComparison.Ordinal);
      
        if (evidenceIndex < 0)
        {
            return Task.FromResult(Error(
                $"Evidence text not found verbatim in source. Make sure the 'evidence' argument is an exact, character-for-character substring of the source text. " +
                $"Check for whitespace, punctuation, and formatting differences."
            ));
        }

        var span = new TextSpan(evidenceIndex, evidenceText.Length);
        var evidence = new RawEvidence(evidenceText, context.SourceContent, span);

        var claim = new RawExtractedClaim(subjectEntity, predicate, objectEntity, objectLiteral, modality, evidence);
        context.RecordedClaims.Add(claim);

        var objectDesc = objectEntity != null 
            ? $"[{objectEntity.DefinedNames[0]}]" 
            : $"\"{objectLiteral}\"";
        
        return Task.FromResult(Success($"Recorded claim: [{subjectName}] {predicate} → {objectDesc} ({modality})"));
    }
}
