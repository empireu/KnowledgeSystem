namespace KnowledgeSystem.Api;

/// <summary>
///     Reason for closing the messaging layer.
/// </summary>
public enum LayerCloseReason
{
    /// <summary>
    ///     Application is shutting down.
    /// </summary>
    Shutdown,
    /// <summary>
    ///     The conversation timed out due to inactivity.
    /// </summary>
    ConversationTimeout,
    /// <summary>
    ///     The conversation ended naturally.
    /// </summary>
    ConversationEnded
}