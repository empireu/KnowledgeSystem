namespace KnowledgeSystem.Plugins.OreDb;

public class OreDbOptions
{
    public const string Section = "ore_db";

    /// <summary>
    ///     Path to the SQLite database file.
    /// </summary>
    public string DatabasePath { get; set; } = "oredb.db";
    
    public bool IntegrateWithAgent { get; set; }
}
