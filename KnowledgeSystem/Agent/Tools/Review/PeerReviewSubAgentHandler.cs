using System.Text;
using KnowledgeSystem.Agent.Config;
using KnowledgeSystem.Agent.Tools.Markers;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.EmdParser.MarkdownTree;
using KnowledgeSystem.Events.Implementation;
using KnowledgeSystem.Provider;
using Microsoft.Extensions.AI;

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Agent.Tools.Review;

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

        var visitedNodes = new HashSet<MarkdownNode>();
        for (var i = startIndex + 1; i < elements.Count; i++)
        {
            var element = elements[i];

            switch (element)
            {
                case FastContextMarker fastContextMarker:
                {
                    sb.AppendLine(fastContextMarker.Output);
                    sb.AppendLine("---");
                    break;
                }
                case RepositoryFetchedNodeMarker repoFetchNodeMarker:
                {
                    // Check if this node is a child of an already added node:
                    var isIncluded = false;
                    var current = repoFetchNodeMarker.Node.RawNode;
                    while (current != null)
                    {
                        if (visitedNodes.Contains(current))
                        {
                            isIncluded = true;
                            break;
                        }

                        current = current.Parent;
                    }

                    if (!isIncluded)
                    {
                        visitedNodes.Add(repoFetchNodeMarker.Node.RawNode);

                        var node = repoFetchNodeMarker.Node;

                        var content = node.Document.Content.Substring(
                            node.RawNode.StartOffset,
                            node.RawNode.EndOffset - node.RawNode.StartOffset
                        );

                        sb.AppendLine(content);
                        sb.AppendLine("---");
                    }

                    break;
                }
                case RepositoryFetchedTextMarker repoFetchTextMarker:
                {
                    sb.AppendLine(repoFetchTextMarker.Content);
                    sb.AppendLine("---");
                    break;
                }
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
        private int _turnCount;
        
        private const int MaxTurns = 5;

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
                OpenAiChatClientFactory.Create(options.Endpoint, options.ApiKey, options.Model),
                new PeerReviewAgent("peer_reviewer"),
                parentRunner,
                reviewContext,
                NullEventManager.Instance,
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
            
            if (++_turnCount > MaxTurns)
            {
                return _subAgentHandler.Error($"Review agent exceeded maximum turn limit ({MaxTurns}).");
            }
            
            var result = await _runner.ExecuteTurn();

            if (result == AgentRunner.TurnStatus.CompletedSuccessfully)
            {
                switch (ReviewContext.FinalStatus)
                {
                    case PeerReviewContext.Status.Approved:
                        return _subAgentHandler.Success("Review passed. Your report has been shown to the user.");
                    case PeerReviewContext.Status.Rejected:
                        return _subAgentHandler.Error(ReviewContext.Feedback ?? "No feedback provided.");
                    case PeerReviewContext.Status.Invalid:
                    default:
                        return _subAgentHandler.Error("Review agent completed without calling approve or reject.");
                }
            }

            if (result == AgentRunner.TurnStatus.CompletedWithError)
            {
                return _subAgentHandler.Error($"Review agent error: {_runner.FinishError?.Message ?? "Unknown error"}");
            }

            return null;
        }

        public Microsoft.Extensions.AI.ChatOptions CreateOptionsForTurn(AgentRunner runner)
        {
            return OpenAiChatOptionsFactory.Create(_options.ProviderOnly, _options.Temperature);
        }
    }
}