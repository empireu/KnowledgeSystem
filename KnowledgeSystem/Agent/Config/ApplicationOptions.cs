using System.ComponentModel.DataAnnotations;
using KnowledgeSystem.Ai;
using KnowledgeSystem.Agents.Context.TokenEstimation;

namespace KnowledgeSystem.Agent.Config;

/// <summary>
///     Temporary, will be implemented by the agent once it's separatd.
/// </summary>
public class ApplicationOptions
{
    public const string Section = "app";

    /// <summary>
    ///     The chat LLM provider configuration.
    /// </summary>
    [Required]
    public ProviderConfig ChatProvider { get; set; } = null!;

    /// <summary>
    ///     Chat request parameters for the main agent.
    /// </summary>
    public ChatOptionsConfig Chat { get; set; } = new();

    [Required]
    public string SystemPromptFile { get; set; } = null!;

    [Required]
    public ChatTemplateFormat Template { get; set; }

    [Required]
    public string TokenizerDir { get; set; } = null!;

    public bool Verbose { get; set; } = false;

    /// <summary>
    ///     The review sub-agent provider. If null, peer review gets disabled, but you also need to make sure the system prompt is consistent.
    /// </summary>
    public ProviderConfig? ReviewProvider { get; set; }

    /// <summary>
    ///     Review sub-agent chat request parameters.
    /// </summary>
    public ChatOptionsConfig ReviewChat { get; set; } = new();

    /// <summary>
    ///     Path to the review sub-agent system prompt file. Must not be null if enabled.
    /// </summary>
    public string? ReviewSystemPromptFile { get; set; }
}
