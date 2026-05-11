namespace KnowledgeSystem.Agents.Context;

public interface ITimelineElement
{
    /// <summary>
    ///     Converts the element to a readable output string.
    /// </summary>
    string ToLogFormat();
}