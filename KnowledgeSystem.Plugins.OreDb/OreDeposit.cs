// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace KnowledgeSystem.Plugins.OreDb;

public class OreDeposit
{
    public int Id { get; set; }

    public int AsteroidId { get; set; }

    public int GameInstanceId { get; set; }

    public required string OreType { get; set; }

    public double Volume { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }

    public bool IsEstimated { get; set; }

    public Asteroid Asteroid { get; set; } = null!;
}
