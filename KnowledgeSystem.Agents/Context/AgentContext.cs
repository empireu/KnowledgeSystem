using Microsoft.Extensions.AI;

// ReSharper disable LoopCanBeConvertedToQuery
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Agents.Context;

public sealed class AgentContext
{
    private readonly List<ITimelineElement> _elementsInternal = [];
    
    /// <summary>
    ///     Chat messages, built on-demand.
    /// </summary>
    private readonly List<ChatMessage> _chatMessages = [];
    
    /// <summary>
    ///     If true, then the <see cref="_chatMessages"/> need to be cleared and re-populated.
    /// </summary>
    private bool _isTimelineDirty;
    
    /// <summary>
    ///     Gets the elements for viewing. 
    /// </summary>
    public IReadOnlyList<ITimelineElement> Elements => _elementsInternal;

    /// <summary>
    ///     Gets the chat messages to be sent.
    /// </summary>
    public IReadOnlyList<ChatMessage> ChatMessages
    {
        get
        {
            if (_isTimelineDirty)
            {
                _chatMessages.Clear();
                for (var index = 0; index < _elementsInternal.Count; index++)
                {
                    var timelineElement = _elementsInternal[index];
                    
                    if (timelineElement is ChatElement chat)
                    {
                        _chatMessages.Add(chat.Message);
                    }
                }

                _isTimelineDirty = false;
            }

            return _chatMessages;
        }
    }
    
    /// <summary>
    ///     Gets the timeline as a mutable list.
    /// </summary>
    public List<ITimelineElement> MutableElements
    {
        get
        {
            _isTimelineDirty = true;
            return _elementsInternal;
        }
    }
    
    /// <summary>
    ///     Adds a element to the context, at the specified index.
    /// </summary>
    /// <param name="element"></param>
    /// <param name="index">If not null, the element will be inserted at the specified index. Otherwise, it will be added to the end of the context.</param>
    public void InsertElement(ITimelineElement element, int? index = null)
    {
        if (index.HasValue)
        {        
            _elementsInternal.Insert(index.Value, element);
        }
        else
        {
            _elementsInternal.Add(element);
        }

        if (element is ChatElement)
        {
            _isTimelineDirty = true;
        }
    }

    public void InsertSystem(string message, int? index = null) => InsertElement(
        new ChatElement(new ChatMessage(ChatRole.System, message)),
        index
    );
    
    public void InsertUser(string message, int? index = null) => InsertElement(
        new ChatElement(new ChatMessage(ChatRole.User, message)),
        index
    );
    
    public void InsertAssistant(string message, int? index = null) => InsertElement(
        new ChatElement(new ChatMessage(ChatRole.Assistant, message)),
        index
    );

    public void InsertAssistant(ChatResponse response, int? index = null)
    {
        // Add all response messages (typically one assistant message)
        foreach (var message in response.Messages)
        {
            InsertElement(new ChatElement(message), index);
            if (index.HasValue)
            {
                index++;
            }
        }
    }

    public void InsertChat(ChatMessage message, int? index = null) => InsertElement(
        new ChatElement(message),
        index
    );

    /// <summary>
    ///     Clears all messages in the chat.
    /// </summary>
    public void ClearAll()
    {
        _elementsInternal.Clear();
        _chatMessages.Clear();
        _isTimelineDirty = false;
    }
}