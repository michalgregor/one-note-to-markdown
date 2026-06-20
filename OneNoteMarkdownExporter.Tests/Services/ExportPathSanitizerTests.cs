using System.IO;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class ExportPathSanitizerTests
{
    [Fact]
    public void SanitizeComponent_WithTrailingSpace_RemovesTrailingSpace()
    {
        var result = ExportPathSanitizer.SanitizeComponent("Folder With Space ");

        result.Should().Be("Folder With Space");
    }

    [Fact]
    public void SanitizeComponent_WithTrailingPeriod_RemovesTrailingPeriod()
    {
        var result = ExportPathSanitizer.SanitizeComponent("Folder With Dot.");

        result.Should().Be("Folder With Dot");
    }

    [Fact]
    public void SanitizeComponent_WithTrailingSpaceAndPeriod_RemovesBoth()
    {
        var result = ExportPathSanitizer.SanitizeComponent("Folder With Space And Dot .");

        result.Should().Be("Folder With Space And Dot");
    }

    [Fact]
    public void SanitizeComponent_WithInvalidCharacters_ReplacesInvalidCharacters()
    {
        var result = ExportPathSanitizer.SanitizeComponent("TSG: Runbook");

        result.Should().Be("TSG_ Runbook");
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("COM2")]
    [InlineData("COM3")]
    [InlineData("COM4")]
    [InlineData("COM5")]
    [InlineData("COM6")]
    [InlineData("COM7")]
    [InlineData("COM8")]
    [InlineData("COM9")]
    [InlineData("COM\u00b9")]
    [InlineData("COM\u00b2")]
    [InlineData("COM\u00b3")]
    [InlineData("LPT1")]
    [InlineData("LPT2")]
    [InlineData("LPT3")]
    [InlineData("LPT4")]
    [InlineData("LPT5")]
    [InlineData("LPT6")]
    [InlineData("LPT7")]
    [InlineData("LPT8")]
    [InlineData("LPT9")]
    [InlineData("LPT\u00b9")]
    [InlineData("LPT\u00b2")]
    [InlineData("LPT\u00b3")]
    public void SanitizeComponent_WithReservedName_AppendsUnderscore(string reservedName)
    {
        var result = ExportPathSanitizer.SanitizeComponent(reservedName);

        result.Should().Be($"{reservedName}_");
    }

    [Theory]
    [InlineData("NUL.txt", "NUL_.txt")]
    [InlineData("CON.md", "CON_.md")]
    [InlineData("lpt1.tar.gz", "lpt1_.tar.gz")]
    public void SanitizeComponent_WithReservedNameAndExtension_ProtectsReservedStem(string name, string expected)
    {
        var result = ExportPathSanitizer.SanitizeComponent(name);

        result.Should().Be(expected);
    }

    [Fact]
    public void SanitizeComponent_WithAllInvalidCharacters_ReturnsUntitled()
    {
        var result = ExportPathSanitizer.SanitizeComponent("<>:\"/\\|?*\0");

        result.Should().Be("Untitled");
    }

    [Fact]
    public void GetSafeMarkdownFilePath_WithNameInsidePathBudget_PreservesFullName()
    {
        var folderPath = CreateTempExportPath();
        var pageName = new string('A', 100);

        var result = ExportPathSanitizer.GetSafeMarkdownFilePath(folderPath, pageName, "page-id");

        Path.GetFileName(result).Should().Be($"{pageName}.md");
        Path.GetFullPath(result).Length.Should().BeLessOrEqualTo(ExportPathSanitizer.MaxWin32PathLength);
    }

    [Fact]
    public void GetSafeMarkdownFilePath_WithPathAboveBudget_ShortensFileName()
    {
        var folderPath = CreateTempExportPath();
        var pageName = new string('A', 260);

        var result = ExportPathSanitizer.GetSafeMarkdownFilePath(folderPath, pageName, "page-id");
        var fileName = Path.GetFileName(result);

        Path.GetFullPath(result).Length.Should().BeLessOrEqualTo(ExportPathSanitizer.MaxWin32PathLength);
        fileName.Should().EndWith(".md");
        Path.GetFileNameWithoutExtension(fileName).Should().MatchRegex("^A+_[0-9a-f]{8}$");
        fileName.Length.Should().BeLessThan(pageName.Length + ".md".Length);
    }

    [Fact]
    public void GetSafeMarkdownFilePath_WithSameStableId_UsesSameShortenedName()
    {
        var folderPath = CreateTempExportPath();
        var pageName = new string('A', 260);

        var first = ExportPathSanitizer.GetSafeMarkdownFilePath(folderPath, pageName, "same-id");
        var second = ExportPathSanitizer.GetSafeMarkdownFilePath(folderPath, pageName, "same-id");

        Path.GetFileName(first).Should().Be(Path.GetFileName(second));
    }

    [Fact]
    public void GetSafeMarkdownFilePath_WithDifferentStableIds_UsesDifferentShortenedNames()
    {
        var folderPath = CreateTempExportPath();
        var pageName = new string('A', 260);

        var first = ExportPathSanitizer.GetSafeMarkdownFilePath(folderPath, pageName, "first-id");
        var second = ExportPathSanitizer.GetSafeMarkdownFilePath(folderPath, pageName, "second-id");

        Path.GetFileName(first).Should().NotBe(Path.GetFileName(second));
    }

    [Fact]
    public void GetSafeDirectoryPath_WithTrailingSpaceParent_RemovesTrailingSpaceComponent()
    {
        var rootPath = CreateTempExportPath();

        var result = ExportPathSanitizer.GetSafeDirectoryPath(rootPath, "Parent Folder ", "parent-id");

        Path.GetFileName(result).Should().Be("Parent Folder");
    }

    [Fact]
    public void GetSafeDirectoryPath_WithTrailingPeriodParent_RemovesTrailingPeriodComponent()
    {
        var rootPath = CreateTempExportPath();

        var result = ExportPathSanitizer.GetSafeDirectoryPath(rootPath, "Parent Folder.", "parent-id");

        Path.GetFileName(result).Should().Be("Parent Folder");
    }

    [Fact]
    public void GetSafePaths_WithLongParentAndLongChild_KeepPathsInsideBudget()
    {
        var rootPath = CreateTempExportPath();
        var parentPath = ExportPathSanitizer.GetSafeDirectoryPath(rootPath, new string('P', 260), "parent-id");
        var childPath = ExportPathSanitizer.GetSafeMarkdownFilePath(parentPath, new string('C', 260), "child-id");

        Path.GetFullPath(parentPath).Length.Should().BeLessOrEqualTo(ExportPathSanitizer.MaxWin32DirectoryPathLength);
        Path.GetFullPath(childPath).Length.Should().BeLessOrEqualTo(ExportPathSanitizer.MaxWin32PathLength);
        Path.GetFileName(parentPath).Should().NotEndWith(" ").And.NotEndWith(".");
        Path.GetFileName(childPath).Should().NotEndWith(" ").And.NotEndWith(".");
    }

    [Theory]
    [InlineData("Project Notes", "_assets_ProjectNotes")]
    [InlineData("2026 planning", "_assets_2026Planning")]
    [InlineData("Q&A / Work", "_assets_QAWork")]
    [InlineData("OneNote Export", "_assets_OneNoteExport")]
    [InlineData("Segun's Notebook", "_assets_SegunsNotebook")]
    [InlineData("Segun’s Notebook", "_assets_SegunsNotebook")]
    [InlineData("Teacher's Aide", "_assets_TeachersAide")]
    public void GetAssetScopeFolderName_WithReadableName_ReturnsPascalCaseSuffix(string scopeName, string expected)
    {
        var result = ExportPathSanitizer.GetAssetScopeFolderName("_assets", scopeName);

        result.Should().Be(expected);
    }

    [Fact]
    public void GetAssetScopeFolderName_WithEmptyCleanName_UsesAssetsFallback()
    {
        var result = ExportPathSanitizer.GetAssetScopeFolderName("_assets", "<>:\"/\\|?*");

        result.Should().Be("_assets_Assets");
    }

    [Fact]
    public void GetSafeAssetScopeDirectoryPath_WithFirstClaim_UsesReadableName()
    {
        var rootPath = CreateTempExportPath();
        var claimedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = ExportPathSanitizer.GetSafeAssetScopeDirectoryPath(rootPath, "_assets", "Project Notes", "first-id", claimedNames);

        Path.GetFileName(result).Should().Be("_assets_ProjectNotes");
    }

    [Fact]
    public void GetSafeAssetScopeDirectoryPath_WithSameLocationCollision_AddsStableHashSuffix()
    {
        var rootPath = CreateTempExportPath();
        var claimedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var first = ExportPathSanitizer.GetSafeAssetScopeDirectoryPath(rootPath, "_assets", "Project Notes", "first-id", claimedNames);
        var second = ExportPathSanitizer.GetSafeAssetScopeDirectoryPath(rootPath, "_assets", "Project Notes", "second-id", claimedNames);

        Path.GetFileName(first).Should().Be("_assets_ProjectNotes");
        Path.GetFileName(second).Should().MatchRegex("^_assets_ProjectNotes_[0-9a-f]{8}$");
        second.Should().NotBe(first);
    }

    [Fact]
    public void GetSafeAssetScopeDirectoryPath_WithDifferentLocations_ReusesReadableName()
    {
        var rootPath = CreateTempExportPath();
        var firstParent = Path.Combine(rootPath, "First");
        var secondParent = Path.Combine(rootPath, "Second");
        var firstClaimedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var secondClaimedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var first = ExportPathSanitizer.GetSafeAssetScopeDirectoryPath(firstParent, "_assets", "Project Notes", "first-id", firstClaimedNames);
        var second = ExportPathSanitizer.GetSafeAssetScopeDirectoryPath(secondParent, "_assets", "Project Notes", "second-id", secondClaimedNames);

        Path.GetFileName(first).Should().Be("_assets_ProjectNotes");
        Path.GetFileName(second).Should().Be("_assets_ProjectNotes");
    }

    private static string CreateTempExportPath()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    }
}
