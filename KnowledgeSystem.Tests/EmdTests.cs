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
        Assert.Equal("path/to/dir/", result.Path);
    }

    [Fact]
    public void TryParse_TrailingBackslash_IsDirectory()
    {
        Assert.True(EmdReferencePath.TryParse("path\\to\\dir\\", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.Directory, result.Type);
        Assert.Equal("path\\to\\dir\\", result.Path);
    }

    [Fact]
    public void TryParse_ExtensionInLastSegment_IsFile()
    {
        Assert.True(EmdReferencePath.TryParse("path/to/File.emd", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.File, result.Type);
        Assert.Equal("path/to/File.emd", result.Path);
    }

    [Fact]
    public void TryParse_NoExtension_IsDirectory()
    {
        Assert.True(EmdReferencePath.TryParse("path/to/Folder", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.Directory, result.Type);
        Assert.Equal("path/to/Folder", result.Path);
    }

    [Fact]
    public void TryParse_ExtensionWithBackslashPath_IsFile()
    {
        Assert.True(EmdReferencePath.TryParse("path\\to\\File.emd", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.File, result.Type);
        Assert.Equal("path\\to\\File.emd", result.Path);
    }

    [Fact]
    public void TryParse_AtSign_IsDefinition()
    {
        Assert.True(EmdReferencePath.TryParse("path/to/File.emd@Basics", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.Definition, result.Type);
        Assert.Equal("path/to/File.emd", result.Path);
        Assert.Equal("Basics", result.Definition);
    }

    [Fact]
    public void TryParse_ColonWithComma_IsOffsets()
    {
        Assert.True(EmdReferencePath.TryParse("path/to/File.emd:10,20", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.Offsets, result.Type);
        Assert.Equal("path/to/File.emd", result.Path);
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
        Assert.True(EmdReferencePath.TryParse("File.emd", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.File, result.Type);
        Assert.Equal("File.emd", result.Path);
    }

    [Fact]
    public void TryParse_SimpleNameNoExtension_IsDirectory()
    {
        Assert.True(EmdReferencePath.TryParse("DirectoryName", out var result));
        Assert.Equal(EmdReferencePath.ReferenceType.Directory, result.Type);
        Assert.Equal("DirectoryName", result.Path);
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
        var result = EmdReferencePath.Parse("path/to/File.emd");
        Assert.Equal(EmdReferencePath.ReferenceType.File, result.Type);
    }

    [Fact]
    public void Parse_InvalidInput_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => EmdReferencePath.Parse(""));
    }
    
    #endregion
}