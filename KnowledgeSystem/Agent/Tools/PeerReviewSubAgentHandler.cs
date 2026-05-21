using System.Text;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Observer;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Ai;
using KnowledgeSystem.Discord;
using Microsoft.Extensions.AI;

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Agent.Tools;

public class PeerReviewSubAgentHandler(AgentTool tool, StringArgument reportArgument, ReviewOptions options) : ToolHandler<ConversationalContext>.SubAgent(tool) {
    public const string ToolId = "submit_with_review";    
    
    public static void Register(AgentToolRegistry<ConversationalContext> registry, ReviewOptions options)
    {
        var reviewTool = new ToolBuilder(ToolId)
            .WithDescription("Submits your message for the user to be peer-reviewed. If it passes, it will be shown to the user immediately. Otherwise, you will get a report on the found issues. Only call if you are responding with any information; don't call if you are just exchanging pleasantries.")
            .WithRequiredStringArgument("report", "Your final report for the user.", out var reportArg)
            .Build();

        var handler = new PeerReviewSubAgentHandler(
            reviewTool,
            reportArg,
            options
        );
        
        registry.RegisterTool(reviewTool, handler);
    }
    
    public override async Task<ISubAgentProxy> BeginSubAgentExecution(
        AgentRunner<ConversationalContext> runner,
        ArgumentExtractionResult args, 
        ConversationalContext runContext,
        string toolCallId,
        CancellationToken cancellationToken)
    {
        var report = reportArgument.GetValue(args);

        if (string.IsNullOrWhiteSpace(report))
        {
            throw new Exception("Report is empty");
        }
        
        var systemPrompt = await File.ReadAllTextAsync(options.SystemPromptFile, cancellationToken);
        
        var sb = new StringBuilder();
        sb.AppendLine(systemPrompt);

        var elements = runContext.ChatContext.MutableElements;
        
        // Distills the effective data used by the main agent:
        var startIndex = elements.FindLastIndex(element => element is ChatElement { Message: var msg } && msg.Role == ChatRole.User);

        if (startIndex == -1)
        {
            throw new Exception("Could not isolate user message");
        }

        for (var i = startIndex + 1; i < elements.Count; i++)
        {
            var element = elements[i];

            if (element is not ChatElement chatElement)
            {
                continue;
            }

            if (chatElement.Message.Role != ChatRole.Tool)
            {
                continue;
            }

            foreach (var text in ChatMessageHelpers.EnumerateTextContents(chatElement.Message))
            {
                sb.AppendLine(text);
                sb.AppendLine();
            }
        }
        
        var reviewContext = new PeerReviewContext(toolCallId, sb.ToString(), report);
        
        return new Proxy(runner, this, reviewContext, options, cancellationToken);
    }
    
    public sealed class Proxy : ISubAgentProxy, AgentRunner.ICompletionFactory
    {
        public PeerReviewContext ReviewContext { get; }
        
        private readonly AgentRunner<ConversationalContext> _parentRunner;
        private readonly PeerReviewSubAgentHandler _subAgentHandler;
        private readonly ReviewOptions _options;
        private readonly AgentRunner<PeerReviewContext> _runner;

        public Proxy(
            AgentRunner<ConversationalContext> parentRunner,
            PeerReviewSubAgentHandler subAgentHandler,
            PeerReviewContext reviewContext, 
            ReviewOptions options, 
            CancellationToken cancellationToken
        )
        {
            ReviewContext = reviewContext;
            _parentRunner = parentRunner;
            _subAgentHandler = subAgentHandler;
            _options = options;

            _runner = new AgentRunner<PeerReviewContext>(
                NullAgentObserver.Instance,
                OpenAiChatClientFactory.Create(options.Endpoint, options.ApiKey, options.Model),
                new PeerReviewAgent("peer_reviewer"),
                parentRunner,
                reviewContext,
                cancellationToken,
                this
            );
        }
        
        public AgentRunner AgentRunner  => _runner;
        
        public async Task<ToolExecutionResult?> StepAsync()
        {
            if (_parentRunner.ActiveToolCalls.Count > 1)
            {
                return _subAgentHandler.Error("Cannot execute review in parallel with other tools!");
            }
            
            var result = await _runner.ExecuteTurn();

            if (result == AgentRunner.TurnStatus.CompletedSuccessfully)
            {
                switch (ReviewContext.FinalStatus)
                {
                    case PeerReviewContext.Status.Approved:
                        return _subAgentHandler.Success("Review passed. Your report has been shown to the user.");
                    case PeerReviewContext.Status.Rejected:
                        return _subAgentHandler.Error(ReviewContext.Feedback);
                    default:
                        throw new Exception($"Invalid review status {ReviewContext.FinalStatus}");
                }
            }

            if (result == AgentRunner.TurnStatus.CompletedWithError)
            {
                throw new Exception($"Review agent error: {_runner.FinishError}");
            }

            return null;
        }

        public Microsoft.Extensions.AI.ChatOptions CreateOptionsForTurn(AgentRunner runner)
        {
            return OpenAiChatOptionsFactory.Create(_options.ProviderOnly, _options.Temperature);
        }
    }
}