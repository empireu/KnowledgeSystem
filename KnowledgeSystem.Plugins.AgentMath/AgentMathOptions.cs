namespace KnowledgeSystem.Plugins.AgentMath;

public class AgentMathOptions
{
    public const string Section = "agentmath";

    /// <summary>
    ///     Maximum seconds a single expression may run before it is abandoned.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    ///     Maximum length of the expression string passed by the agent.
    /// </summary>
    public int MaxExpressionLength { get; set; } = 2000;

    /// <summary>
    ///     Maximum length of the formatted result returned to the agent.
    /// </summary>
    public int MaxOutputLength { get; set; } = 4000;
}
