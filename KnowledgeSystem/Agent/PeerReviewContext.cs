using System.Diagnostics;

namespace KnowledgeSystem.Agent;

public sealed class PeerReviewContext : ConversationalContext
{
    public enum Status
    {
        Invalid,
        Approved,
        Rejected
    }
    
    public Status FinalStatus
    {
        get => field == Status.Invalid ? throw new InvalidOperationException("Cannot get results from peer review agent before it's done.") : field;
        set
        {
            if (field != Status.Invalid)
            {
                throw new Exception("Multiple set peer review status");
            }

            field = value;
        }
    }

    public string Feedback
    {
        get
        {
            if (FinalStatus == Status.Approved)
            {
                throw new InvalidOperationException("Cannot get feedback from peer review agent that approved the report.");
            }
            
            Debug.Assert(field != null);
            
            return field;
        }
        set
        {
            if (field != null)
            {
                throw new Exception("Multiple set peer review feedback");
            }

            field = value;
        }
    }
    
    public PeerReviewContext(string systemPrompt, string report)
    {
        ChatContext.InsertSystem(systemPrompt);
        ChatContext.InsertUser(report);
    }
}