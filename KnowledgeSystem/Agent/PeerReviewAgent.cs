using KnowledgeSystem.Agent.Tools;
using KnowledgeSystem.Agents.Orchestration;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Agent;

public sealed class PeerReviewAgent: Agent<PeerReviewContext>
{
    public PeerReviewAgent(string agentId) : base(agentId)
    {
        ReviewFlagToolHandler<PeerReviewContext>.Register(
            ToolRegistry,
            "approve",
            "Approves the user's report as correct and factual.",
            OnApproved
        );
        
        ReviewFlagToolHandler<PeerReviewContext>.Register(
            ToolRegistry,
            "reject",
            "Rejects the user's report as incorrect/having mistakes.",
            OnRejected
        );
    }
    
    private static string OnApproved(PeerReviewContext context)
    {
        context.FinalStatus = PeerReviewContext.Status.Approved;
        
        return "Approved. You don't need to give any feedback."; // In our architecture, this won't actually get sent to the model
    }

    private static string OnRejected(PeerReviewContext context)
    {
        context.FinalStatus = PeerReviewContext.Status.Rejected;
        
        return "Flagged. Now, please write your feedback for the USER.";
    }
    
    public override Task<AgentCallbackResult> HandleToolFinish(AgentRunner<PeerReviewContext> runner)
    {
        if (runner.ExecutionContext.FinalStatus == PeerReviewContext.Status.Approved)
        {
            // End execution immediately:
            return Task.FromResult(AgentCallbackResult.Break);
        }
        
        return base.HandleToolFinish(runner);
    }
    
    public override Task<AgentCallbackResult> HandleCompletion(AgentRunner<PeerReviewContext> runner, ChatResponse response)
    {
        runner.ExecutionContext.Feedback = response.Text;
        return Task.FromResult(AgentCallbackResult.Break);
    }
}