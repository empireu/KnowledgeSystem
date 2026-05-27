using KnowledgeSystem.Agents.Orchestration;

namespace KnowledgeSystem.Api;

/// <summary>
///     Pipeline assembled to respond to one message.
/// </summary>
public interface IResponsePipeline
{
    /// <summary>
    ///     Responds to the message.
    /// </summary>
    /// <returns></returns>
    public Task ExecuteAsync();
    
    /// <summary>
    ///     Wraps a method as the execution pipeline.s
    /// </summary>
    public static IResponsePipeline Wrap(Func<Task> function) => new Wrapper(function);

    private sealed class Wrapper(Func<Task> function) : IResponsePipeline
    {
        public async Task ExecuteAsync()
        {
            var task = function();

            await task;
        }
    }
}