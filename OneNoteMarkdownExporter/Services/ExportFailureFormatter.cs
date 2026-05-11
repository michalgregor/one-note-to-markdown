using System;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services
{
    public static class ExportFailureFormatter
    {
        public static string FormatPageFailure(OneNoteItem page, string targetPath, Exception exception)
        {
            return string.Join(Environment.NewLine,
                $"Page: {page.Name}",
                $"Page ID: {page.Id}",
                $"Target: {targetPath}",
                $"Error: {exception.Message}");
        }

        public static string FormatGeneralFailure(string operation, Exception exception)
        {
            return string.Join(Environment.NewLine,
                $"Operation: {operation}",
                $"Error: {exception.Message}");
        }
    }
}
