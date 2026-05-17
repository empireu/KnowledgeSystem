namespace KnowledgeSystem.Discord.Observer;

public sealed partial class DiscordObserver
{
    private enum ToolState
    {
        Running,
        Completed,
        Failed
    }

    private sealed class ToolStatusEntry(string toolId, string arguments, ToolState state)
    {
        public string ToolId { get; } = toolId;
        public string Arguments { get; } = arguments;
        public ToolState State { get; set; } = state;
        public string? Error { get; set; }

        public string ToMarkdown() => State switch
        {
            ToolState.Running => $"◌ *{ToolId}*({Arguments})",
            ToolState.Completed => $"✓ *{ToolId}*({Arguments})",
            ToolState.Failed => $"✗ *{ToolId}*({Arguments}) — {Error}",
            _ => $"*{ToolId}*({Arguments})"
        };

        public string ToCompactMarkdown() => State switch
        {
            ToolState.Running => $"◌ *{ToolId}*",
            ToolState.Completed => $"✓ *{ToolId}*",
            ToolState.Failed => $"✗ *{ToolId}* — {Error}",
            _ => $"*{ToolId}*"
        };
    }
}