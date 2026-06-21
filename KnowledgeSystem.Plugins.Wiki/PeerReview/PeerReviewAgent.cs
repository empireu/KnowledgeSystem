using KnowledgeSystem.Agents.Orchestration;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Plugins.Wiki.PeerReview;

public sealed class PeerReviewAgent : Agent<PeerReviewContext>
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
    
    public override Task<AgentCallbackResult> HandleToolCompletion(AgentRunner<PeerReviewContext> runner, ChatResponse response)
    {
        // Capture any text the LLM wrote alongside the tool call (e.g. review feedback with reject)
        if (!string.IsNullOrWhiteSpace(response.Text))
        {
            runner.ExecutionContext.PendingFeedback = response.Text;
        }
        
        return Task.FromResult(AgentCallbackResult.Continue);
    }
    
    public override Task<AgentCallbackResult> HandleToolFinish(AgentRunner<PeerReviewContext> runner)
    {
        var status = runner.ExecutionContext.FinalStatus;
        
        if (status == PeerReviewContext.Status.Invalid)
        {
            // No flag tool was called yet, continue to let the LLM decide:
            return Task.FromResult(AgentCallbackResult.Continue);
        }
        
        if (status == PeerReviewContext.Status.Approved)
        {
            // End execution immediately:
            return Task.FromResult(AgentCallbackResult.Break);
        }
        
        // Rejected: if feedback was already captured alongside the tool call, promote it and end.
        // Otherwise, continue so the LLM can write feedback.
        if (runner.ExecutionContext.PendingFeedback is { } feedback)
        {
            runner.ExecutionContext.Feedback = feedback;
            return Task.FromResult(AgentCallbackResult.Break);
        }
        
        return Task.FromResult(AgentCallbackResult.Continue);
    }
    
    public override Task<AgentCallbackResult> HandleCompletion(AgentRunner<PeerReviewContext> runner, ChatResponse response)
    {
        var status = runner.ExecutionContext.FinalStatus;
        
        if (status == PeerReviewContext.Status.Invalid)
        {
            // LLM output text without calling approve/reject first.
            //Prompt it to use a tool:
            runner.ExecutionContext.Timeline.InsertAssistant("I must call either approve or reject before writing my final output.");
            return Task.FromResult(AgentCallbackResult.Continue);
        }
        
        if (status == PeerReviewContext.Status.Approved)
        {
            // Already approved, no feedback needed:
            return Task.FromResult(AgentCallbackResult.Break);
        }
        
        // Rejected: capture feedback and end:
        runner.ExecutionContext.Feedback = response.Text;
        return Task.FromResult(AgentCallbackResult.Break);
    }
}