using KnowledgeSystem.EmdParser.ExtendedMarkdown;

// ReSharper disable EntityFramework.ModelValidation.UnlimitedStringLength
// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace KnowledgeSystem.Retrieval.Persistent;

/// <summary>
///     Represents a known document in the repository, tracked for sync diffing.
/// </summary>
public class DocumentRecord
{
    /// <summary>
    ///     The repository-relative normalized path.
    ///     This is the primary key and matches <see cref="EmdReferencePath.RepositoryRelativePath"/>.
    /// </summary>
    public string Path { get; set; } = null!;

    /// <summary>
    ///     All chunks belonging to this document.
    /// </summary>
    public ICollection<ChunkRecord> Chunks { get; set; } = [];
}
