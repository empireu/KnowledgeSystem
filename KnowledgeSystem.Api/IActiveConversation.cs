namespace KnowledgeSystem.Api;

/// <summary>
///     Represents the integration layer for an ongoing conversation with an agent, scoped to a discord thread.
///     Manages the lifetime and execution of the methods in the <see cref="Layer"/>.
/// </summary>
public interface IActiveConversation
{
    /// <summary>
    ///     Gets the channel ID of the thread.
    /// </summary>
    public ulong ChannelId { get; }
    
    /// <summary>
    ///     Gets the time offset for automatic expiration.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    ///     Delays the expiration of the activity.
    /// </summary>
    public void TouchActivity();
}