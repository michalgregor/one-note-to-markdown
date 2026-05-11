using System.IO;
using FluentAssertions;
using OneNoteMarkdownExporter.Models;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class ExportFailureFormatterTests
{
    [Fact]
    public void FormatPageFailure_IncludesPageNameIdTargetAndError()
    {
        var page = new OneNoteItem
        {
            Id = "page-id",
            Name = "Page Name",
            Type = OneNoteItemType.Page
        };
        var exception = new IOException("Could not write file");

        var result = ExportFailureFormatter.FormatPageFailure(page, @"C:\Export\Page Name.md", exception);

        result.Should().Contain("Page: Page Name");
        result.Should().Contain("Page ID: page-id");
        result.Should().Contain(@"Target: C:\Export\Page Name.md");
        result.Should().Contain("Error: Could not write file");
    }

    [Fact]
    public void FormatGeneralFailure_IncludesOperationAndError()
    {
        var exception = new InvalidOperationException("Export stopped unexpectedly");

        var result = ExportFailureFormatter.FormatGeneralFailure("Export", exception);

        result.Should().Contain("Operation: Export");
        result.Should().Contain("Error: Export stopped unexpectedly");
    }
}
