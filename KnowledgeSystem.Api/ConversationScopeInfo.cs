namespace KnowledgeSystem.Api;

public readonly struct ConversationScopeInfo(ulong id, ConversationScopeInfo.Type scopeType) : IEquatable<ConversationScopeInfo>
{
    public enum Type
    {
        /// <summary>
        ///     The conversation lives throughout the execution of a slash command.
        /// </summary>
        Single,
        /// <summary>
        ///     The conversation is scoped to a thread channel.
        /// </summary>
        Channel
    }

    public ulong Id { get; } = id;

    public Type ScopeType { get; } = scopeType;

    public bool Equals(ConversationScopeInfo other)
    {
        return Id == other.Id && ScopeType == other.ScopeType;
    }

    public override bool Equals(object? obj)
    {
        return obj is ConversationScopeInfo other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, (int)ScopeType);
    }

    public static bool operator ==(ConversationScopeInfo left, ConversationScopeInfo right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ConversationScopeInfo left, ConversationScopeInfo right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return $"ConversationTarget[{ScopeType}, {Id}]";
    }
}