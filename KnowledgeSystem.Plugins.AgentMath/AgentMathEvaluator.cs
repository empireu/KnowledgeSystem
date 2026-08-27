using System.Reflection;
using System.Text.Json;
using Acornima.Ast;
using Jint;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Plugins.AgentMath;

public sealed record AgentMathEvaluation(bool IsSuccessful, string? Value, string? Error);

public sealed class AgentMathEvaluator(IOptions<AgentMathOptions> options, ILogger<AgentMathEvaluator> logger)
{
    private const string ResourceName = "KnowledgeSystem.Plugins.AgentMath.math.min.js";

    private static readonly Prepared<Script> MathJsScript = Engine.PrepareScript(LoadMathJs());

    private static string LoadMathJs()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    public async Task<AgentMathEvaluation> EvaluateAsync(string expression, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds));

        return await Task.Run(() =>
        {
            string? result = null;
            Exception? error = null;

            var worker = new Thread(() =>
            {
                try
                {
                    result = EvaluateOnEngine(expression, timeout);
                }
                catch (Exception e)
                {
                    error = e;
                }
            })
            {
                IsBackground = true,
                Name = "agentmath-eval"
            };

            worker.Start();

            if (!worker.Join(timeout))
            {
                logger.LogWarning("AgentMath evaluation abandoned after {timeout} seconds", timeout.TotalSeconds);

                return new AgentMathEvaluation(false, null, $"evaluation timed out after {timeout.TotalSeconds:0} seconds");
            }

            if (error != null)
            {
                return new AgentMathEvaluation(false, null, error.Message);
            }

            var output = result ?? string.Empty;

            if (output.Length > options.Value.MaxOutputLength)
            {
                output = output[..options.Value.MaxOutputLength] + "...";
            }

            return new AgentMathEvaluation(true, output, null);
        }, cancellationToken);
    }

    private static string EvaluateOnEngine(string expression, TimeSpan timeout)
    {
        var engine = new Engine(engineOptions => engineOptions
            .TimeoutInterval(timeout)
            .MaxStatements(100_000_000)
            .LimitMemory(256 * 1024 * 1024));

        engine.Execute(MathJsScript);

        var json = JsonSerializer.Serialize(expression);

        var wrapper =
            "math.config({number: 'BigNumber'});" +
            $"var __r = math.evaluate({json});" +
            "var __out = (__r && __r.isNode) ? __r.toString() : math.format(__r, {precision: 14});" +
            "__out;";

        var value = engine.Evaluate(wrapper);

        return value.ToString();
    }
}
