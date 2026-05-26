namespace KnowledgeSystem.Retrieval.Api.Store;

public readonly struct StoreCapabilityType(string id) : IEquatable<StoreCapabilityType>
{
    public string Id { get; } = id;

    public bool Equals(StoreCapabilityType other)
    {
        return Id == other.Id;
    }

    public override bool Equals(object? obj)
    {
        return obj is StoreCapabilityType other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    public static bool operator ==(StoreCapabilityType left, StoreCapabilityType right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(StoreCapabilityType left, StoreCapabilityType right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return $"Capability[{Id}]";
    }
}