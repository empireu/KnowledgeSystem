// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace KnowledgeSystem.Plugins.OreDb;

public class GameInstance
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public List<Asteroid> Asteroids { get; set; } = [];
}
