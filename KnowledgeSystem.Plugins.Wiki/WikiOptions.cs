using System.ComponentModel.DataAnnotations;
using KnowledgeSystem.Agents.Context.TokenEstimation;
using KnowledgeSystem.Ai;
using KnowledgeSystem.Plugins.Wiki.Memory;

// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace KnowledgeSystem.Plugins.Wiki;

public class WikiOptions
{
    public const string Section = "wiki";

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
    public string RepoPath { get; set; } = null!;

    [Required]
    public string SystemPromptFile { get; set; } = null!;

    [Required]
    public ChatTemplateFormat Template { get; set; }

    [Required]
    public string TokenizerDir { get; set; } = null!;

    public bool Verbose { get; set; }

    /// <summary>
    ///     The review sub-agent provider. If null, peer review gets disabled, but you also need to make sure the system prompt is consistent.
    /// </summary>
    public ProviderConfig? ReviewProvider { get; set; }

    /// <summary>
    ///     Review sub-agent chat request parameters.
    /// </summary>
    public ChatOptionsConfig Review { get; set; } = new();

    /// <summary>
    ///     Path to the review sub-agent system prompt file. Must not be null if enabled.
    /// </summary>
    public string? ReviewSystemPromptFile { get; set; }
    
    public MemorySystemConfig? Memory { get; set; }
}
