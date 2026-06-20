using System.IO;
using FluentAssertions;
using OneNoteMarkdownExporter.Models;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class ExportPathPlannerTests
{
    [Fact]
    public void GetCentralizedAssetsFolderPath_WithDefaultOptions_UsesOutputAssetsFolder()
    {
        var outputPath = CreateTempExportPath();
        var planner = new ExportPathPlanner(outputPath, new ExportOptions());

        var result = planner.GetCentralizedAssetsFolderPath();

        result.Should().Be(Path.Combine(outputPath, "_assets"));
    }

    [Fact]
    public void GetCentralizedAssetsFolderPath_WithRelativeCustomPath_ResolvesFromOutputRoot()
    {
        var outputPath = CreateTempExportPath();
        var planner = new ExportPathPlanner(outputPath, new ExportOptions { AssetsFolderPath = @"media\images" });

        var result = planner.GetCentralizedAssetsFolderPath();

        result.Should().Be(Path.Combine(outputPath, "media", "images"));
    }

    [Fact]
    public void GetScopedAssetsFolderPath_ForNotebook_UsesNotebookFolderAndName()
    {
        var outputPath = CreateTempExportPath();
        var notebook = new OneNoteItem { Id = "notebook-id", Name = "Project Notes", Type = OneNoteItemType.Notebook };
        var planner = new ExportPathPlanner(outputPath, new ExportOptions { AssetOrganizationMode = AssetOrganizationMode.Notebook });
        var notebookFolder = planner.GetContainerFolderPath(outputPath, notebook);

        var result = planner.GetScopedAssetsFolderPath(notebookFolder, notebook);

        result.Should().Be(Path.Combine(notebookFolder, "_assets_ProjectNotes"));
    }

    [Fact]
    public void GetScopedAssetsFolderPath_ForSection_UsesSectionFolderAndName()
    {
        var outputPath = CreateTempExportPath();
        var section = new OneNoteItem { Id = "section-id", Name = "Meeting Notes", Type = OneNoteItemType.Section };
        var planner = new ExportPathPlanner(outputPath, new ExportOptions { AssetOrganizationMode = AssetOrganizationMode.Section });
        var sectionFolder = Path.Combine(outputPath, "Notebook", "Meeting Notes");

        var result = planner.GetScopedAssetsFolderPath(sectionFolder, section);

        result.Should().Be(Path.Combine(sectionFolder, "_assets_MeetingNotes"));
    }

    [Fact]
    public void CreatePageContext_ForPageAssets_UsesPageFolderAndRelativePath()
    {
        var outputPath = CreateTempExportPath();
        var page = new OneNoteItem { Id = "page-id", Name = "Page Name", Type = OneNoteItemType.Page };
        var planner = new ExportPathPlanner(outputPath, new ExportOptions { AssetOrganizationMode = AssetOrganizationMode.Page });
        var sectionFolder = Path.Combine(outputPath, "Notebook", "Section");
        var assetsFolder = planner.GetScopedAssetsFolderPath(sectionFolder, page);

        var result = planner.CreatePageContext(page, sectionFolder, assetsFolder);

        result.MarkdownFilePath.Should().Be(Path.Combine(sectionFolder, "Page Name.md"));
        result.ChildPageFolderPath.Should().Be(Path.Combine(sectionFolder, "Page Name"));
        result.AssetsFolderPath.Should().Be(Path.Combine(sectionFolder, "_assets_PageName"));
        result.RelativeAssetsPath.Should().Be("_assets_PageName");
    }

    [Fact]
    public void CreatePageContext_ForNestedSubpageAssets_UsesSubpageFolderAndRelativePath()
    {
        var outputPath = CreateTempExportPath();
        var subpage = new OneNoteItem { Id = "subpage-id", Name = "Sub Page", Type = OneNoteItemType.Page };
        var planner = new ExportPathPlanner(outputPath, new ExportOptions { AssetOrganizationMode = AssetOrganizationMode.Page });
        var subpageFolder = Path.Combine(outputPath, "Notebook", "Section", "Parent Page");
        var assetsFolder = planner.GetScopedAssetsFolderPath(subpageFolder, subpage);

        var result = planner.CreatePageContext(subpage, subpageFolder, assetsFolder);

        result.MarkdownFilePath.Should().Be(Path.Combine(subpageFolder, "Sub Page.md"));
        result.AssetsFolderPath.Should().Be(Path.Combine(subpageFolder, "_assets_SubPage"));
        result.RelativeAssetsPath.Should().Be("_assets_SubPage");
    }

    [Fact]
    public void GetScopedAssetsFolderPath_WithSameLocationCollision_AddsHashOnlyToSecondFolder()
    {
        var outputPath = CreateTempExportPath();
        var firstPage = new OneNoteItem { Id = "first-page-id", Name = "Project Notes", Type = OneNoteItemType.Page };
        var secondPage = new OneNoteItem { Id = "second-page-id", Name = "Project Notes", Type = OneNoteItemType.Page };
        var planner = new ExportPathPlanner(outputPath, new ExportOptions { AssetOrganizationMode = AssetOrganizationMode.Page });
        var sectionFolder = Path.Combine(outputPath, "Notebook", "Section");

        var first = planner.GetScopedAssetsFolderPath(sectionFolder, firstPage);
        var second = planner.GetScopedAssetsFolderPath(sectionFolder, secondPage);

        Path.GetFileName(first).Should().Be("_assets_ProjectNotes");
        Path.GetFileName(second).Should().MatchRegex("^_assets_ProjectNotes_[0-9a-f]{8}$");
    }

    private static string CreateTempExportPath()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    }
}
