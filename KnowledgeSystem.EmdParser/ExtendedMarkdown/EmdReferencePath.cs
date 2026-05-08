namespace KnowledgeSystem.EmdParser.ExtendedMarkdown;

public readonly struct EmdReferencePath(string path, EmdReferencePath.ReferenceType type, string definition, int startOffset, int endOffset)
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
    ///     Path to the directory or file, plus additional data. Meaning is based on the <see cref="Type"/>.
    /// </summary>
    public readonly string Path = path;
    
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
}