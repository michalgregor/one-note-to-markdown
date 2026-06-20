using System.IO;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class AssetPathResolverTests
{
    [Fact]
    public void ResolveAssetsFolderPath_WithNullPath_ReturnsOutputAssetsFolder()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = AssetPathResolver.ResolveAssetsFolderPath(outputPath, null);

        result.Should().Be(Path.Combine(outputPath, "_assets"));
    }

    [Fact]
    public void ResolveAssetsFolderPath_WithWhitespacePath_ReturnsOutputAssetsFolder()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = AssetPathResolver.ResolveAssetsFolderPath(outputPath, "   ");

        result.Should().Be(Path.Combine(outputPath, "_assets"));
    }

    [Fact]
    public void ResolveAssetsFolderPath_WithRelativePath_ResolvesFromOutputPath()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "export");

        var result = AssetPathResolver.ResolveAssetsFolderPath(outputPath, @"media\images");

        result.Should().Be(Path.Combine(outputPath, "media", "images"));
    }

    [Fact]
    public void ResolveAssetsFolderPath_WithAbsolutePath_ReturnsAbsolutePath()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "export");
        var assetsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "assets");

        var result = AssetPathResolver.ResolveAssetsFolderPath(outputPath, assetsPath);

        result.Should().Be(assetsPath);
    }

    [Fact]
    public void PrepareAssetsFolder_WithMissingFolder_CreatesDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var outputPath = Path.Combine(tempRoot, "export");

        try
        {
            var result = AssetPathResolver.PrepareAssetsFolder(outputPath, "media");

            Directory.Exists(result).Should().BeTrue();
            result.Should().Be(Path.Combine(outputPath, "media"));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void PrepareAssetsFolder_WithExistingDirectory_ReturnsDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var outputPath = Path.Combine(tempRoot, "export");
        var assetsPath = Path.Combine(tempRoot, "existing-assets");
        var existingAssetPath = Path.Combine(assetsPath, "existing.png");

        try
        {
            Directory.CreateDirectory(assetsPath);
            File.WriteAllBytes(existingAssetPath, new byte[] { 9, 9, 9 });

            var result = AssetPathResolver.PrepareAssetsFolder(outputPath, assetsPath);

            result.Should().Be(assetsPath);
            Directory.Exists(result).Should().BeTrue();
            File.Exists(existingAssetPath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void PrepareAssetsFolder_WithExistingFile_ThrowsIOException()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var outputPath = Path.Combine(tempRoot, "export");
        var filePath = Path.Combine(tempRoot, "assets-file");

        try
        {
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(filePath, "not a directory");

            var act = () => AssetPathResolver.PrepareAssetsFolder(outputPath, filePath);

            act.Should().Throw<IOException>()
                .WithMessage("Assets folder path points to an existing file: *");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void GetRelativeAssetsPath_WithExternalAssetsFolder_ReturnsForwardSlashRelativePath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var markdownFolderPath = Path.Combine(tempRoot, "export", "Notebook");
        var assetsFolderPath = Path.Combine(tempRoot, "shared", "assets");

        var result = AssetPathResolver.GetRelativeAssetsPath(markdownFolderPath, assetsFolderPath);

        result.Should().Be("../../shared/assets");
    }
}
