using System.IO;
using FluentAssertions;
using OneNoteMarkdownExporter.Models;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class ExportServiceTests
{
    [Theory]
    [InlineData(AssetOrganizationMode.Centralized, @"..\..\_assets", @"_assets")]
    [InlineData(AssetOrganizationMode.Notebook, @"..\_assets_ProjectNotebook", @"Project Notebook\_assets_ProjectNotebook")]
    [InlineData(AssetOrganizationMode.Section, @"_assets_MeetingNotes", @"Project Notebook\Meeting Notes\_assets_MeetingNotes")]
    [InlineData(AssetOrganizationMode.Page, @"_assets_PlanningPage", @"Project Notebook\Meeting Notes\_assets_PlanningPage")]
    public async Task ExportSelectedAsync_WithAssetOrganizationMode_UsesExpectedAssetsFolderAndRelativePath(
        AssetOrganizationMode mode,
        string expectedRelativeAssetsPath,
        string expectedAssetsRelativeFolder)
    {
        var outputPath = CreateTempExportPath();
        var source = new FakeOneNoteExportSource();
        var converter = new RecordingMarkdownConverter();
        var linter = new PassthroughMarkdownLintService();
        var service = new ExportService(source, converter, linter);
        var notebooks = CreateSelectedHierarchy();
        var options = new ExportOptions
        {
            OutputPath = outputPath,
            AssetOrganizationMode = mode,
            ApplyLinting = false,
            Overwrite = true
        };

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            result.ExportedPages.Should().Be(1);
            converter.Calls.Should().ContainSingle();
            converter.Calls[0].AssetsFolder.Should().Be(Path.Combine(outputPath, expectedAssetsRelativeFolder));
            converter.Calls[0].RelativeAssetsPath.Should().Be(expectedRelativeAssetsPath.Replace("\\", "/"));
            File.ReadAllText(Path.Combine(outputPath, "Project Notebook", "Meeting Notes", "Planning Page.md"))
                .Should().Be($"asset:{expectedRelativeAssetsPath.Replace("\\", "/")}");
            Directory.Exists(Path.Combine(outputPath, expectedAssetsRelativeFolder)).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithCentralizedCustomAssetsFolder_UsesCustomFolder()
    {
        var tempRoot = CreateTempExportPath();
        var outputPath = Path.Combine(tempRoot, "export");
        var assetsPath = Path.Combine(tempRoot, "shared-assets");
        var source = new FakeOneNoteExportSource();
        var converter = new RecordingMarkdownConverter();
        var linter = new PassthroughMarkdownLintService();
        var service = new ExportService(source, converter, linter);
        var notebooks = CreateSelectedHierarchy();
        var options = new ExportOptions
        {
            OutputPath = outputPath,
            AssetsFolderPath = assetsPath,
            AssetOrganizationMode = AssetOrganizationMode.Centralized,
            ApplyLinting = false,
            Overwrite = true
        };

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            converter.Calls.Should().ContainSingle();
            converter.Calls[0].AssetsFolder.Should().Be(assetsPath);
            converter.Calls[0].RelativeAssetsPath.Should().Be("../../../shared-assets");
            Directory.Exists(assetsPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Theory]
    [InlineData(AssetOrganizationMode.Centralized, @"_assets")]
    [InlineData(AssetOrganizationMode.Notebook, @"Project Notebook\_assets_ProjectNotebook")]
    [InlineData(AssetOrganizationMode.Section, @"Project Notebook\Meeting Notes\_assets_MeetingNotes")]
    [InlineData(AssetOrganizationMode.Page, @"Project Notebook\Meeting Notes\_assets_PlanningPage")]
    public async Task ExportSelectedAsync_WithTextOnlyPage_DoesNotCreateAssetsFolder(AssetOrganizationMode mode, string expectedAssetsRelativeFolder)
    {
        var outputPath = CreateTempExportPath();
        var source = new FakeOneNoteExportSource();
        var converter = new RecordingMarkdownConverter();
        var linter = new PassthroughMarkdownLintService();
        var service = new ExportService(source, converter, linter);
        var notebooks = CreateSelectedHierarchy();
        var options = new ExportOptions
        {
            OutputPath = outputPath,
            AssetOrganizationMode = mode,
            ApplyLinting = false,
            Overwrite = true
        };

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            Directory.Exists(Path.Combine(outputPath, expectedAssetsRelativeFolder)).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WhenConverterWritesAsset_CreatesAssetsFolder()
    {
        var outputPath = CreateTempExportPath();
        var source = new FakeOneNoteExportSource();
        var converter = new AssetWritingMarkdownConverter();
        var linter = new PassthroughMarkdownLintService();
        var service = new ExportService(source, converter, linter);
        var notebooks = CreateSelectedHierarchy();
        var options = new ExportOptions
        {
            OutputPath = outputPath,
            AssetOrganizationMode = AssetOrganizationMode.Page,
            ApplyLinting = false,
            Overwrite = true
        };
        var expectedAssetsFolder = Path.Combine(outputPath, "Project Notebook", "Meeting Notes", "_assets_PlanningPage");

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            Directory.Exists(expectedAssetsFolder).Should().BeTrue();
            File.Exists(Path.Combine(expectedAssetsFolder, "image_0001.png")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithExistingFileAtGeneratedAssetsPath_FailsPageClearly()
    {
        var outputPath = CreateTempExportPath();
        var assetsFilePath = Path.Combine(outputPath, "Project Notebook", "Meeting Notes", "_assets_PlanningPage");
        var source = new FakeOneNoteExportSource();
        var converter = new RecordingMarkdownConverter();
        var linter = new PassthroughMarkdownLintService();
        var service = new ExportService(source, converter, linter);
        var notebooks = CreateSelectedHierarchy();
        var options = new ExportOptions
        {
            OutputPath = outputPath,
            AssetOrganizationMode = AssetOrganizationMode.Page,
            ApplyLinting = false,
            Overwrite = true
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assetsFilePath)!);
            File.WriteAllText(assetsFilePath, "not a folder");

            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeFalse();
            result.FailedPages.Should().Be(1);
            result.Failures.Should().ContainSingle()
                .Which.Should().Contain("Assets folder path points to an existing file:");
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithExistingFileAtCustomCentralizedAssetsPath_FailsClearly()
    {
        var tempRoot = CreateTempExportPath();
        var outputPath = Path.Combine(tempRoot, "export");
        var assetsFilePath = Path.Combine(tempRoot, "custom-assets");
        var source = new FakeOneNoteExportSource();
        var converter = new RecordingMarkdownConverter();
        var linter = new PassthroughMarkdownLintService();
        var service = new ExportService(source, converter, linter);
        var notebooks = CreateSelectedHierarchy();
        var options = new ExportOptions
        {
            OutputPath = outputPath,
            AssetsFolderPath = assetsFilePath,
            AssetOrganizationMode = AssetOrganizationMode.Centralized,
            ApplyLinting = false,
            Overwrite = true
        };

        try
        {
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(assetsFilePath, "not a folder");

            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Assets folder path points to an existing file:");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    private static List<OneNoteItem> CreateSelectedHierarchy()
    {
        return new List<OneNoteItem>
        {
            new OneNoteItem
            {
                Id = "notebook-id",
                Name = "Project Notebook",
                Type = OneNoteItemType.Notebook,
                IsSelected = true,
                Children =
                {
                    new OneNoteItem
                    {
                        Id = "section-id",
                        Name = "Meeting Notes",
                        Type = OneNoteItemType.Section,
                        Children =
                        {
                            new OneNoteItem
                            {
                                Id = "page-id",
                                Name = "Planning Page",
                                Type = OneNoteItemType.Page
                            }
                        }
                    }
                }
            }
        };
    }

    private static string CreateTempExportPath()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    }

    private sealed class FakeOneNoteExportSource : IOneNoteExportSource
    {
        public List<OneNoteItem> GetNotebookHierarchy()
        {
            return CreateSelectedHierarchy();
        }

        public string GetPageContent(string pageId)
        {
            return "<one:Page xmlns:one=\"http://schemas.microsoft.com/office/onenote/2013/onenote\" />";
        }

        public string? GetBinaryPageContent(string pageId, string callbackId)
        {
            return null;
        }
    }

    private sealed class RecordingMarkdownConverter : IMarkdownContentConverter
    {
        public List<ConverterCall> Calls { get; } = new();

        public string Convert(string pageXml, string assetsFolder, string relativeAssetsPath, BinaryContentFetcher? binaryContentFetcher = null, string? pagePrefix = null)
        {
            Calls.Add(new ConverterCall(assetsFolder, relativeAssetsPath));
            return $"asset:{relativeAssetsPath}";
        }
    }

    private sealed class AssetWritingMarkdownConverter : IMarkdownContentConverter
    {
        public string Convert(string pageXml, string assetsFolder, string relativeAssetsPath, BinaryContentFetcher? binaryContentFetcher = null, string? pagePrefix = null)
        {
            Directory.CreateDirectory(assetsFolder);
            File.WriteAllBytes(Path.Combine(assetsFolder, "image_0001.png"), new byte[] { 1, 2, 3 });
            return $"![image]({relativeAssetsPath}/image_0001.png)";
        }
    }

    private sealed record ConverterCall(string AssetsFolder, string RelativeAssetsPath);

    private sealed class PassthroughMarkdownLintService : IMarkdownLintService
    {
        public bool IsAvailable => true;
        public string UnavailableReason => string.Empty;

        public string LintContent(string markdown)
        {
            return markdown;
        }
    }
}
