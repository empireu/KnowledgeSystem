namespace KnowledgeSystem.Api;

/// <summary>
///     Tracks executing responses to dispatch cancellation, such as when the application is shutting down.
/// </summary>
public interface IResponseTracker
{
    /// <summary>
    ///     Adds a run. The <see cref="key"/> must be unique, or this will throw.
    /// </summary>
    public void Add(ulong key, ActiveRunInfo info);

    /// <summary>
    ///     Removes a run. Returns false if the run was not registered.
    /// </summary>
    public bool Remove(ulong key);
}