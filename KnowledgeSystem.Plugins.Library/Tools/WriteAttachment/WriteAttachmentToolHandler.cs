using System.Text.RegularExpressions;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Events.Api;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Library.Tools.WriteAttachment;

public sealed class WriteAttachmentToolHandler(
    AgentTool tool,
    StringArgument nameArgument,
    StringArgument contentArgument
) : ToolHandler<BasicContext>.Plain(tool)
{
    private const int MaxNameLength = 64;
    private const int MaxContentChars = 65536;

    private static readonly Regex SafeNameRegex = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider)
    {
        var attachTool = new ToolBuilder("write_attachment")
            .WithDescription("Queues a file to be attached to the final response message. Use this when the answer is too long for the message itself: put the full content here and keep the reply as a short summary.")
            .WithRequiredStringArgument("name", "File name for the attachment, e.g. 'full_report.md'. Letters, digits, '.', '_' and '-' only.", out var nameArg)
            .WithRequiredStringArgument("content", "The full file content, up to 65536 characters.", out var contentArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<WriteAttachmentToolHandler>(
            serviceProvider,
            attachTool,
            nameArg,
            contentArg
        );

        registry.RegisterTool(attachTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var name = nameArgument.GetValue(args);
        var content = contentArgument.GetValue(args);

        if (name.Length == 0 || name.Length > MaxNameLength || !SafeNameRegex.IsMatch(name))
        {
            return Error($"write_attachment: Invalid file name '{name}'. Use letters, digits, '.', '_' and '-' only, up to {MaxNameLength} characters.");
        }

        if (content.Length > MaxContentChars)
        {
            return Error($"write_attachment: Content too long ({content.Length} chars, limit {MaxContentChars}).");
        }

        await runner.EventManager.SendAsync(new AddAttachmentEvent(name, content), cancellationToken);

        return Success($"Attachment '{name}' queued ({content.Length} chars). It will be attached to the final response.");
    }
}
