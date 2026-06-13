using System.Collections.Generic;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services
{
    /// <summary>
    /// Abstraction over the OneNote COM interop layer so that callers
    /// can be unit tested with a fake implementation instead of a live OneNote instance.
    /// </summary>
    public interface IOneNoteService
    {
        List<OneNoteItem> GetNotebookHierarchy();

        /// <summary>Forces OneNote to synchronize the given hierarchy node (notebook/section/page).</summary>
        void SyncHierarchy(string objectId);

        /// <summary>Navigates to a page, forcing cloud-synced content (including binaries) to load.</summary>
        void NavigateToPage(string pageId);

        void PublishPage(string pageId, string outputPath);

        string GetPageContent(string pageId);

        /// <summary>Retrieves base64 binary content referenced by a callbackID, or null if unavailable.</summary>
        string? GetBinaryPageContent(string pageId, string callbackId);

        void UpdatePageContent(string xml);

        bool ExpandCollapsedParagraphs(string pageId);

        /// <summary>True if this service caused OneNote to launch (it was not already running).</summary>
        bool LaunchedOneNote { get; }

        /// <summary>Minimizes the OneNote window, but only if this service launched OneNote.</summary>
        void MinimizeWindowIfLaunched();

        /// <summary>Closes OneNote, but only if this service launched it (leaves a user's session alone).</summary>
        void ShutdownIfLaunched();
    }
}
