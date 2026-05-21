using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Agents.Context.TokenEstimation;

/// <summary>
///     Estimates the number of tokens in a context.
/// </summary>
public interface ITokenEstimator : IDisposable
{
    int CountTokens(IEnumerable<ChatMessage> messages);
}