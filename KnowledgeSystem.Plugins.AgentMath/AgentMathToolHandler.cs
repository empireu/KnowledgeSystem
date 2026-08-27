using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Plugins.AgentMath;

public sealed class AgentMathToolHandler(
    AgentTool tool,
    StringArgument expressionArgument,
    AgentMathEvaluator evaluator,
    IOptions<AgentMathOptions> options
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider)
    {
        var calculateTool = new ToolBuilder("math_calculate")
            .WithDescription("Evaluates a mathematical expression with exact decimal arithmetic. Supports numbers, units (e.g. '1000 kg to t'), matrices, common functions (sqrt, log, sin, ...), and symbolic operations such as derivative(\"x^2\", \"x\") or simplify(\"x*x + 2*x\"). The expression must be self-contained: no variables, only concrete values.")
            .WithRequiredStringArgument("expression", "The math expression to evaluate, e.g. '0.1 + 0.2', 'sqrt(144)', 'derivative(\"x^2\", \"x\")'.", out var expressionArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<AgentMathToolHandler>(
            serviceProvider,
            calculateTool,
            expressionArg
        );

        registry.RegisterTool(calculateTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var expression = expressionArgument.GetValue(args);

        if (expression.Length == 0)
        {
            return Error("math_calculate: expression is empty.");
        }

        if (expression.Length > options.Value.MaxExpressionLength)
        {
            return Error($"math_calculate: expression too long ({expression.Length} chars, limit {options.Value.MaxExpressionLength}).");
        }

        var evaluation = await evaluator.EvaluateAsync(expression, cancellationToken);

        return evaluation.IsSuccessful
            ? Success($"math_calculate: {evaluation.Value}")
            : Error($"math_calculate: {evaluation.Error}");
    }
}
