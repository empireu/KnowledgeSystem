using System.Text.Json;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Agents.Tools;

/// <summary>
///     An <see cref="AIFunction"/> that wraps a pre-built JSON schema for the function parameters.
///     This is needed because our <see cref="ToolBuilder"/> constructs schemas manually, rather than relying on <see cref="AIFunctionFactory"/> reflection-based schema generation.
///     This was not refactored away because I realize it actually gives us more finely-grained control over tools, which is good.
/// </summary>
internal sealed class AiFunctionSchema(string name, string description, JsonElement parametersSchema) : AIFunction
{
    public override string Name { get; } = name;

    public override string Description { get; } = description;

    public override JsonElement JsonSchema => parametersSchema;

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments args, CancellationToken cancellationToken)
    {
        // This AIFunction is only used for schema description, not for invocation.
        // Our tool execution is handled by ToolHandler:
        throw new InvalidOperationException(
            $"{nameof(AiFunctionSchema)} is description-only and should not be invoked directly. " +
            "Tool execution is handled by the ToolHandler infrastructure."
        );
    }
}