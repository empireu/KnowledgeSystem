using KnowledgeSystem.Plugins.Library;

namespace KnowledgeSystem.Plugins.Wiki;

public sealed class PeerReviewContext : ConversationalContext
{
    public string ToolCallId { get; }
    public string Report { get; }

    public enum Status
    {
        Invalid,
        Approved,
        Rejected
    }

    public Status FinalStatus
    {
        get;
        set
        {
            if (field == Status.Invalid)
            {
                field = value;
            }
            else if (field != value)
            {
                throw new InvalidOperationException($"Peer review status already set to {field}, cannot change to {value}.");
            }
            
            // Same value as already set
        }
    }

    /// <summary>
    ///     Text the LLM wrote alongside a flag tool call, captured before the tool executes.
    ///     Promoted to <see cref="Feedback"/> in <see cref="PeerReviewAgent.HandleToolFinish"/> if rejected.
    /// </summary>
    public string? PendingFeedback { get; set; }

    public string? Feedback
    {
        get
        {
            if (FinalStatus == Status.Approved)
            {
                throw new InvalidOperationException("Cannot get feedback from peer review agent that approved the report.");
            }
            
            return field;
        }
        set;
    }

    public PeerReviewContext(string toolCallId, string systemPrompt, string report)
    {
        ToolCallId = toolCallId;
        Report = report;
        ChatContext.InsertSystem(systemPrompt);
        ChatContext.InsertUser(report);
    }
}