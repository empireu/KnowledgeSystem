// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace KnowledgeSystem.Plugins.OreDb;

public class Asteroid
{
    public int Id { get; set; }

    public int GameInstanceId { get; set; }

    public required string Name { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }

    public float Size { get; set; }

    public GameInstance GameInstance { get; set; } = null!;

    public List<OreDeposit> OreDeposits { get; set; } = [];
}
