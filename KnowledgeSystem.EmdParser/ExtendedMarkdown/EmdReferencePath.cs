namespace KnowledgeSystem.EmdParser.ExtendedMarkdown;

public readonly struct EmdReferencePath(string repositoryRelativePath, EmdReferencePath.ReferenceType type, string definition, int startOffset, int endOffset) : IEquatable<EmdReferencePath>
{
    public enum ReferenceType : byte
    {
        /// <summary>
        ///     The reference points to a directory of documents.
        /// </summary>
        Directory,
        /// <summary>
        ///     The path points to an entire document.
        /// </summary>
        File,
        /// <summary>
        ///     The path points to a defined section within a document (e.g. <c>".../File.emd@Basics"</c>).
        /// </summary>
        Definition,
        /// <summary>
        ///     The path points to a span within a document (e.g. <c>".../File.emd:10,20"</c>), with the first number being the start offset (inclusive), and the second number being the end offset (exclusive). 
        /// </summary>
        Offsets
    }

    /// <summary>
    ///     Creates a file reference.
    /// </summary>
    /// <param name="repositoryRelativePath">The repository-relative normalized path to the file.</param>
    /// <returns></returns>
    public static EmdReferencePath CreateFile(string repositoryRelativePath) => new(repositoryRelativePath,
        ReferenceType.File,
        definition: string.Empty,
        startOffset: 0,
        endOffset: 0
    );
    
    /// <summary>
    ///     Creates a reference towards a definition in a file.
    /// </summary>
    /// <param name="repositoryRelativePath">The repository-relative normalized path to the file.</param>
    /// <param name="definition">The definition found in the file.</param>
    /// <returns></returns>
    public static EmdReferencePath CreateDefinition(string repositoryRelativePath, string definition) => new(repositoryRelativePath,
        ReferenceType.Definition,
        definition: definition,
        startOffset: 0,
        endOffset: 0
    );
    
    /// <summary>
    ///     Path to the directory or file, plus additional data. Meaning is based on the <see cref="Type"/>.
    /// </summary>
    public readonly string RepositoryRelativePath = repositoryRelativePath;
    
    /// <summary>
    ///     The type of reference.
    /// </summary>
    public readonly ReferenceType Type = type;

    /// <summary>
    ///     Definition for references of type <see cref="ReferenceType.Definition"/>.
    /// </summary>
    public string Definition => Type == ReferenceType.Definition
        ? definition
        : throw new InvalidOperationException($"Cannot get referenced definition for {Type} reference");
    
    /// <summary>
    ///     Start offset of the content's span in the document (inclusive), for references of type <see cref="ReferenceType.Offsets"/>.
    /// </summary>
    public int StartOffset => Type == ReferenceType.Offsets
        ? startOffset
        :  throw new InvalidOperationException($"Cannot get start offset for {Type} reference");
    
    /// <summary>
    ///     End offset of the content's span in the document (exclusive), for references of type <see cref="ReferenceType.Offsets"/>.
    /// </summary>
    public int EndOffset => Type == ReferenceType.Offsets
        ? endOffset
        :  throw new InvalidOperationException($"Cannot get end offset for {Type} reference");

    /// <summary>
    ///     Parses a reference string into an <see cref="EmdReferencePath"/>.
    /// </summary>
    public static EmdReferencePath Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return TryParse(input, out var result) 
            ? result 
            : throw new FormatException($"Invalid reference string: {input}");
    }

    /// <summary>
    ///     Attempts to parse a reference string into an <see cref="EmdReferencePath"/>.
    /// </summary>
    /// <returns>True if the parsing was successful. Otherwise, false.</returns>
    public static bool TryParse(string? input, out EmdReferencePath result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var colonIndex = input.LastIndexOf(':');
        if (colonIndex >= 0)
        {
            var pathPart = input.AsSpan(0, colonIndex);
            var offsetPart = input.AsSpan(colonIndex + 1);
            var commaIndex = offsetPart.IndexOf(',');
           
            if (commaIndex >= 0 && int.TryParse(offsetPart[..commaIndex], out var startOffset) && int.TryParse(offsetPart[(commaIndex + 1)..], out var endOffset))
            {
                result = new EmdReferencePath(
                    pathPart.ToString(),
                    ReferenceType.Offsets,
                    definition: string.Empty,
                    startOffset,
                    endOffset
                );
                
                return true;
            }
        }

        var atIndex = input.LastIndexOf('@');
        if (atIndex >= 0)
        {
            var pathPart = input.AsSpan(0, atIndex);
            var definitionPart = input.AsSpan(atIndex + 1);

            if (pathPart.Length > 0 && definitionPart.Length > 0)
            {
                result = new EmdReferencePath(
                    pathPart.ToString(),
                    ReferenceType.Definition,
                    definitionPart.ToString(),
                    startOffset: 0,
                    endOffset: 0
                );
                
                return true;
            }
        }

        if (input.EndsWith('/') || input.EndsWith('\\'))
        {
            result = new EmdReferencePath(input, ReferenceType.Directory, definition: string.Empty, startOffset: 0, endOffset: 0);
            return true;
        }

        var lastSlashIndex = input.LastIndexOfAny(['/', '\\']);
        var lastSegment = lastSlashIndex >= 0 
            ? input.AsSpan(lastSlashIndex + 1) 
            : input.AsSpan();
        
        var dotIndex = lastSegment.LastIndexOf('.');
        var type = dotIndex > 0 && dotIndex < lastSegment.Length - 1
            ? ReferenceType.File
            : ReferenceType.Directory;

        result = new EmdReferencePath(input, type, definition: string.Empty, startOffset: 0, endOffset: 0);
        return true;
    }

    public bool Equals(EmdReferencePath other)
    {
        if (RepositoryRelativePath != other.RepositoryRelativePath)
        {
            return false;
        }

        if (Type != other.Type)
        {
            return false;
        }

        return Type switch
        {
            ReferenceType.Directory => true,
            ReferenceType.File => true,
            ReferenceType.Definition => Definition == other.Definition,
            ReferenceType.Offsets => StartOffset == other.StartOffset && EndOffset == other.EndOffset,
            _ => throw new ArgumentOutOfRangeException($"Invalid reference type {Type}")
        };
    }

    public override bool Equals(object? obj)
    {
        return obj is EmdReferencePath other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(RepositoryRelativePath);
        hashCode.Add(Type);

        switch (Type)
        {
            case ReferenceType.Definition:
                hashCode.Add(Definition);
                break;
            case ReferenceType.Offsets:
                hashCode.Add(StartOffset);
                hashCode.Add(EndOffset);
                break;
            case ReferenceType.Directory:
            case ReferenceType.File:
            default:
                // Ignored
                break;
        }
        
        return hashCode.ToHashCode();
    }

    public static bool operator ==(EmdReferencePath left, EmdReferencePath right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EmdReferencePath left, EmdReferencePath right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Returns a <see cref="ReferenceType.File"/> reference pointing to the document that this reference resides in.
    ///     Valid for <see cref="ReferenceType.File"/>, <see cref="ReferenceType.Definition"/>, and <see cref="ReferenceType.Offsets"/> references.
    /// </summary>
    public EmdReferencePath GetFile()
    {
        return Type switch
        {
            ReferenceType.File => this,
            ReferenceType.Definition or ReferenceType.Offsets => new EmdReferencePath(
                RepositoryRelativePath,
                ReferenceType.File,
                definition: string.Empty,
                startOffset: 0, 
                endOffset: 0
            ),
            _ => throw new InvalidOperationException($"Cannot get file reference for {Type} reference")
        };
    }

    /// <summary>
    ///     Returns a <see cref="ReferenceType.Directory"/> reference pointing to the directory containing this reference.
    ///     For <see cref="ReferenceType.Directory"/> references, returns itself.
    ///     For other types, extracts the parent directory from the path.
    /// </summary>
    public EmdReferencePath GetDirectory()
    {
        if (Type == ReferenceType.Directory)
        {
            return this;
        }

        var lastSlash = RepositoryRelativePath.LastIndexOfAny(['/', '\\']);
        var dirPath = lastSlash >= 0
            ? RepositoryRelativePath[..(lastSlash + 1)]
            : string.Empty;

        return new EmdReferencePath(
            dirPath,
            ReferenceType.Directory,
            definition: string.Empty,
            startOffset: 0,
            endOffset: 0
        );
    }
}