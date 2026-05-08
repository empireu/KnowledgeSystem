using KnowledgeSystem.EmdParser.ExtendedMarkdown;

namespace KnowledgeSystem.Tests;

public class EmdTests
{
    #region EmdReferencePath
    
    [Fact]
    public void TryParse_TrailingSlash_IsDirectory()
    {
        Assert.True(EmdReferencePath.TryParse("path/to/dir/", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.Directory, result.Type);
        Assert.Equal("path/to/dir/", result.RepositoryRelativePath);
    }

    [Fact]
    public void TryParse_TrailingBackslash_IsDirectory()
    {
        Assert.True(EmdReferencePath.TryParse(@"path\to\dir\", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.Directory, result.Type);
        Assert.Equal(@"path\to\dir\", result.RepositoryRelativePath);
    }

    [Fact]
    public void TryParse_ExtensionInLastSegment_IsFile()
    {
        Assert.True(EmdReferencePath.TryParse("path/to/File.md", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.File, result.Type);
        Assert.Equal("path/to/File.md", result.RepositoryRelativePath);
    }

    [Fact]
    public void TryParse_NoExtension_IsDirectory()
    {
        Assert.True(EmdReferencePath.TryParse("path/to/Folder", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.Directory, result.Type);
        Assert.Equal("path/to/Folder", result.RepositoryRelativePath);
    }

    [Fact]
    public void TryParse_ExtensionWithBackslashPath_IsFile()
    {
        Assert.True(EmdReferencePath.TryParse(@"path\to\File.md", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.File, result.Type);
        Assert.Equal(@"path\to\File.md", result.RepositoryRelativePath);
    }

    [Fact]
    public void TryParse_AtSign_IsDefinition()
    {
        Assert.True(EmdReferencePath.TryParse("path/to/File.md@Basics", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.Definition, result.Type);
        Assert.Equal("path/to/File.md", result.RepositoryRelativePath);
        Assert.Equal("Basics", result.Definition);
    }

    [Fact]
    public void TryParse_ColonWithComma_IsOffsets()
    {
        Assert.True(EmdReferencePath.TryParse("path/to/File.md:10,20", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.Offsets, result.Type);
        Assert.Equal("path/to/File.md", result.RepositoryRelativePath);
        Assert.Equal(10, result.StartOffset);
        Assert.Equal(20, result.EndOffset);
    }

    [Fact]
    public void TryParse_NullInput_ReturnsFalse()
    {
        Assert.False(EmdReferencePath.TryParse(null, out _));
    }

    [Fact]
    public void TryParse_EmptyInput_ReturnsFalse()
    {
        Assert.False(EmdReferencePath.TryParse("", out _));
    }

    [Fact]
    public void TryParse_WhitespaceInput_ReturnsFalse()
    {
        Assert.False(EmdReferencePath.TryParse("   ", out _));
    }

    [Fact]
    public void TryParse_SimpleFileName_IsFile()
    {
        Assert.True(EmdReferencePath.TryParse("File.md", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.File, result.Type);
        Assert.Equal("File.md", result.RepositoryRelativePath);
    }

    [Fact]
    public void TryParse_SimpleNameNoExtension_IsDirectory()
    {
        Assert.True(EmdReferencePath.TryParse("DirectoryName", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.Directory, result.Type);
        Assert.Equal("DirectoryName", result.RepositoryRelativePath);
    }

    [Fact]
    public void TryParse_DotAtStartOfSegment_IsDirectory()
    {
        Assert.True(EmdReferencePath.TryParse("path/to/.hidden", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.Directory, result.Type);
    }

    [Fact]
    public void Parse_ValidInput_ReturnsReference()
    {
        var result = EmdReferencePath.Parse("path/to/File.md");
        Assert.Equal(EmdReferencePath.ReferenceType.File, result.Type);
    }

    [Fact]
    public void Parse_InvalidInput_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => EmdReferencePath.Parse(""));
    }
    
    #endregion

    #region EmdReferencePath.GetFile

    [Fact]
    public void GetFile_OnFileRef_ReturnsSameRef()
    {
        var file = EmdReferencePath.CreateFile("Docs/Guide.md");
        var result = file.GetFile();
        Assert.Equal(file, result);
    }

    [Fact]
    public void GetFile_OnDefinitionRef_ReturnsFileRef()
    {
        var def = EmdReferencePath.CreateDefinition("Docs/Guide.md", "Basics");
        var result = def.GetFile();
        Assert.Equal(EmdReferencePath.ReferenceType.File, result.Type);
        Assert.Equal("Docs/Guide.md", result.RepositoryRelativePath);
    }

    [Fact]
    public void GetFile_OnOffsetsRef_ReturnsFileRef()
    {
        var offset = new EmdReferencePath("Docs/Guide.md", EmdReferencePath.ReferenceType.Offsets, definition: string.Empty, startOffset: 10, endOffset: 20);
        var result = offset.GetFile();
        Assert.Equal(EmdReferencePath.ReferenceType.File, result.Type);
        Assert.Equal("Docs/Guide.md", result.RepositoryRelativePath);
    }

    [Fact]
    public void GetFile_OnDirectoryRef_Throws()
    {
        var dir = new EmdReferencePath("Docs/", EmdReferencePath.ReferenceType.Directory, definition: string.Empty, startOffset: 0, endOffset: 0);
        Assert.Throws<InvalidOperationException>(() => dir.GetFile());
    }

    #endregion

    #region EmdReferencePath.GetDirectory

    [Fact]
    public void GetDirectory_OnDirectoryRef_ReturnsSameRef()
    {
        var dir = new EmdReferencePath("Docs/Sub/", EmdReferencePath.ReferenceType.Directory, definition: string.Empty, startOffset: 0, endOffset: 0);
        var result = dir.GetDirectory();
        Assert.Equal(dir, result);
    }

    [Fact]
    public void GetDirectory_OnFileRef_ExtractsParentDir()
    {
        var file = EmdReferencePath.CreateFile("Docs/Sub/Guide.md");
        var result = file.GetDirectory();
        Assert.Equal(EmdReferencePath.ReferenceType.Directory, result.Type);
        Assert.Equal("Docs/Sub/", result.RepositoryRelativePath);
    }

    [Fact]
    public void GetDirectory_OnDefinitionRef_ExtractsParentDir()
    {
        var def = EmdReferencePath.CreateDefinition("Other/File.md", "Setup");
        var result = def.GetDirectory();
        Assert.Equal(EmdReferencePath.ReferenceType.Directory, result.Type);
        Assert.Equal("Other/", result.RepositoryRelativePath);
    }

    [Fact]
    public void GetDirectory_OnRootFile_ReturnsEmptyDir()
    {
        var file = EmdReferencePath.CreateFile("Guide.md");
        var result = file.GetDirectory();
        Assert.Equal(EmdReferencePath.ReferenceType.Directory, result.Type);
        Assert.Equal(string.Empty, result.RepositoryRelativePath);
    }

    #endregion

    #region EmdReferencePath.CreateFile / CreateDefinition

    [Fact]
    public void CreateFile_SetsTypeAndPath()
    {
        var result = EmdReferencePath.CreateFile("Path/To/File.md");
        Assert.Equal(EmdReferencePath.ReferenceType.File, result.Type);
        Assert.Equal("Path/To/File.md", result.RepositoryRelativePath);
    }

    [Fact]
    public void CreateDefinition_SetsTypePathAndDefinition()
    {
        var result = EmdReferencePath.CreateDefinition("Path/To/File.md", "Basics");
        Assert.Equal(EmdReferencePath.ReferenceType.Definition, result.Type);
        Assert.Equal("Path/To/File.md", result.RepositoryRelativePath);
        Assert.Equal("Basics", result.Definition);
    }

    #endregion

    #region EmdReferencePath Equality

    [Fact]
    public void Equality_SameFileRefs_AreEqual()
    {
        var a = EmdReferencePath.CreateFile("Docs/Guide.md");
        var b = EmdReferencePath.CreateFile("Docs/Guide.md");
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equality_DifferentPaths_NotEqual()
    {
        var a = EmdReferencePath.CreateFile("Docs/A.md");
        var b = EmdReferencePath.CreateFile("Docs/B.md");
        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void Equality_SameDefinitionRefs_AreEqual()
    {
        var a = EmdReferencePath.CreateDefinition("Docs/Guide.md", "Basics");
        var b = EmdReferencePath.CreateDefinition("Docs/Guide.md", "Basics");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentDefinitions_NotEqual()
    {
        var a = EmdReferencePath.CreateDefinition("Docs/Guide.md", "Basics");
        var b = EmdReferencePath.CreateDefinition("Docs/Guide.md", "Setup");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_FileAndDefinition_NotEqual()
    {
        var file = EmdReferencePath.CreateFile("Docs/Guide.md");
        var def = EmdReferencePath.CreateDefinition("Docs/Guide.md", "Basics");
        Assert.NotEqual(file, def);
    }

    #endregion

    #region EmdRepository.NormalizePath

    [Fact]
    public void NormalizePath_Backslashes_ToForwardSlashes()
    {
        Assert.Equal("a/b/c", EmdRepository.NormalizePath(@"a\b\c"));
    }

    [Fact]
    public void NormalizePath_NoBackslashes_Unchanged()
    {
        Assert.Equal("a/b/c", EmdRepository.NormalizePath("a/b/c"));
    }

    [Fact]
    public void NormalizePath_MixedSeparators_Normalizes()
    {
        Assert.Equal("a/b/c/d", EmdRepository.NormalizePath(@"a\b/c\d"));
    }

    #endregion

    #region EmdRepository.GetRepositoryRelativePath

    [Fact]
    public void GetRepositoryRelativePath_SubdirectoryFile_ReturnsRelative()
    {
        var result = EmdRepository.GetRepositoryRelativePath("C:/repo", "C:/repo/Docs/Guide.md");
        Assert.Equal("Docs/Guide.md", result);
    }

    [Fact]
    public void GetRepositoryRelativePath_RootFile_ReturnsFileName()
    {
        var result = EmdRepository.GetRepositoryRelativePath("C:/repo", "C:/repo/README.md");
        Assert.Equal("README.md", result);
    }

    [Fact]
    public void GetRepositoryRelativePath_DeeplyNested_ReturnsRelative()
    {
        var result = EmdRepository.GetRepositoryRelativePath("C:/repo", "C:/repo/A/B/C/File.md");
        Assert.Equal("A/B/C/File.md", result);
    }

    #endregion
}