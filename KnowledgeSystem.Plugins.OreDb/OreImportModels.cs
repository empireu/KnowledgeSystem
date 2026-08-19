namespace KnowledgeSystem.Plugins.OreDb;

public sealed record ImportedOreDeposit(string OreType, double Volume, double X, double Y, double Z, bool IsEstimated);

public sealed record ImportedAsteroid(string Name, double X, double Y, double Z, float Size, IReadOnlyList<ImportedOreDeposit> OreDeposits);
