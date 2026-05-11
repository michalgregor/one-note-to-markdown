using FluentAssertions;
using OneNoteMarkdownExporter.Models;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class ExportSelectionHelperTests
{
    [Fact]
    public void CountPagesToExport_WithSelectedContainer_CountsAllDescendantPages()
    {
        var notebook = new OneNoteItem
        {
            Name = "Notebook",
            Type = OneNoteItemType.Notebook,
            IsSelected = true,
            Children =
            {
                new OneNoteItem
                {
                    Name = "Section",
                    Type = OneNoteItemType.Section,
                    Children =
                    {
                        new OneNoteItem { Name = "First Page", Type = OneNoteItemType.Page },
                        new OneNoteItem { Name = "Second Page", Type = OneNoteItemType.Page }
                    }
                }
            }
        };

        var result = ExportSelectionHelper.CountPagesToExport(new[] { notebook });

        result.Should().Be(2);
    }

    [Fact]
    public void CountPagesToExport_WithSelectedParentPage_CountsParentAndSubpagesWithoutMutatingSelection()
    {
        var childPage = new OneNoteItem { Name = "Child Page", Type = OneNoteItemType.Page };
        var parentPage = new OneNoteItem
        {
            Name = "Parent Page",
            Type = OneNoteItemType.Page,
            IsSelected = true,
            Children = { childPage }
        };

        var result = ExportSelectionHelper.CountPagesToExport(new[] { parentPage });

        result.Should().Be(2);
        childPage.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void CountPagesToExport_WithOnlySelectedSubpage_DoesNotCountUnselectedParentPage()
    {
        var childPage = new OneNoteItem { Name = "Child Page", Type = OneNoteItemType.Page, IsSelected = true };
        var parentPage = new OneNoteItem
        {
            Name = "Parent Page",
            Type = OneNoteItemType.Page,
            Children = { childPage }
        };

        var result = ExportSelectionHelper.CountPagesToExport(new[] { parentPage });

        result.Should().Be(1);
    }

    [Fact]
    public void CountPagesToExport_WithSelectedEmptyContainer_ReturnsZero()
    {
        var section = new OneNoteItem
        {
            Name = "Empty Section",
            Type = OneNoteItemType.Section,
            IsSelected = true
        };

        var result = ExportSelectionHelper.CountPagesToExport(new[] { section });

        result.Should().Be(0);
    }

    [Fact]
    public void CountItemsToExport_WithSelectedParentPage_CountsImplicitSubpageItems()
    {
        var parentPage = new OneNoteItem
        {
            Name = "Parent Page",
            Type = OneNoteItemType.Page,
            IsSelected = true,
            Children =
            {
                new OneNoteItem { Name = "Child Page", Type = OneNoteItemType.Page }
            }
        };

        var result = ExportSelectionHelper.CountItemsToExport(new[] { parentPage });

        result.Should().Be(2);
    }

    [Fact]
    public void HasSelectedDescendants_WithNestedSelectedChild_ReturnsTrue()
    {
        var section = new OneNoteItem
        {
            Name = "Section",
            Type = OneNoteItemType.Section,
            Children =
            {
                new OneNoteItem
                {
                    Name = "Parent Page",
                    Type = OneNoteItemType.Page,
                    Children =
                    {
                        new OneNoteItem { Name = "Child Page", Type = OneNoteItemType.Page, IsSelected = true }
                    }
                }
            }
        };

        var result = ExportSelectionHelper.HasSelectedDescendants(section);

        result.Should().BeTrue();
    }
}
