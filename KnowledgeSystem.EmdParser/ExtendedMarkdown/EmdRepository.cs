namespace KnowledgeSystem.EmdParser.ExtendedMarkdown;

/// <summary>
///     In-memory data structure for a directory of files (documents).
///     Each document can have references to other documents as well.
///     Each document is parsed and chunked.
/// </summary>
/// <param name="rootDirectory"></param>
/// <param name="documents"></param>
public sealed class EmdRepository(string rootDirectory, Dictionary<EmdReferencePath, EmdDocument> documents)
{
    /// <summary>
    ///     The disk path to the directory where the repository is stored.
    /// </summary>
    public readonly string RootDirectory = rootDirectory;

    /// <summary>
    ///     Contains all documents in the repo.
    /// </summary>
    public readonly Dictionary<EmdReferencePath, EmdDocument> Documents = documents;
    
    public static async Task<EmdRepository> LoadAsync(string rootDirectory, Chunker chunker, int maxTasks = 4, CancellationToken cancellationToken = default)
    {
        var documents = new Dictionary<EmdReferencePath, EmdDocument>();
        var repo = new EmdRepository(rootDirectory, documents);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxTasks,
            CancellationToken = cancellationToken
        };
        
        var obj = new object();
        await Parallel.ForEachAsync(Directory.GetFiles(rootDirectory, "*.md", SearchOption.AllDirectories), options,
            async (filePath, token) =>
            {
                var relativePath = GetRepositoryRelativePath(rootDirectory, filePath);
                var content = await File.ReadAllTextAsync(filePath, token);
                var document = EmdDocument.Parse(relativePath, content);
                var key = EmdReferencePath.CreateFile(relativePath);

                lock (obj)
                {
                    documents.Add(key, document);
                }
            }
        );

        VerifyDependencies(repo);

        // Chunk and hash after all documents are loaded and dependencies verified:
        foreach (var document in documents.Values)
        {
            document.GenerateChunksAndLookups(chunker);
        }

        return repo;
    }

    /// <summary>
    ///     Verifies that all declared dependency references across all documents resolve to actual documents, definitions, or directories in the repository.
    /// </summary>
    private static void VerifyDependencies(EmdRepository repo)
    {
        foreach (var document in repo.Documents.Values)
        {
            foreach (var dependency in document.AttachedNodes.Values.SelectMany(node => node.DeclaredDependencyRefs))
            {
                switch (dependency.Type)
                {
                    case EmdReferencePath.ReferenceType.File:
                    case EmdReferencePath.ReferenceType.Offsets:
                    {
                        if (!repo.Documents.ContainsKey(dependency.GetFile()))
                        {
                            throw new KeyNotFoundException($"Document \"{document.Path}\" references file \"{dependency.RepositoryRelativePath}\" which was not found in the repository.");
                        }
                        break;
                    }
                    case EmdReferencePath.ReferenceType.Definition:
                    {
                        if (!repo.Documents.TryGetValue(dependency.GetFile(), out var targetDoc))
                        {
                            throw new KeyNotFoundException($"Document \"{document.Path}\" references definition \"{dependency.Definition}\" in file \"{dependency.RepositoryRelativePath}\" which was not found in the repository.");
                        }
                        
                        if (!targetDoc.NodesWithDefinition.ContainsKey(dependency))
                        {
                            throw new KeyNotFoundException($"Document \"{document.Path}\" references definition \"{dependency.Definition}\" in file \"{dependency.RepositoryRelativePath}\" which does not define it.");
                        }
                        
                        break;
                    }
                    case EmdReferencePath.ReferenceType.Directory:
                    {
                        var dirPath = dependency.RepositoryRelativePath;
                        var found = repo.Documents.Keys.Any(fileKey => fileKey.RepositoryRelativePath.StartsWith(dirPath, StringComparison.OrdinalIgnoreCase));
                        
                        if (!found)
                        {
                            throw new KeyNotFoundException($"Document \"{document.Path}\" references directory \"{dirPath}\" which contains no documents in the repository.");
                        }
                        
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException($"Invalid reference type {dependency.Type}");
                }
            }
        }
    }

    /// <summary>
    ///     Computes a repository-relative path from an absolute disk path.
    /// </summary>
    public static string GetRepositoryRelativePath(string rootDirectory, string absolutePath)
    {
        var normalizedRoot = NormalizePath(Path.GetFullPath(rootDirectory));
        var normalizedPath = NormalizePath(Path.GetFullPath(absolutePath));

        if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath[normalizedRoot.Length..];
        }

        return normalizedPath.TrimStart('/');
    }

    /// <summary>
    ///     Normalizes directory separators to forward slashes.
    /// </summary>
    public static string NormalizePath(string path) => path.Replace('\\', '/');
}