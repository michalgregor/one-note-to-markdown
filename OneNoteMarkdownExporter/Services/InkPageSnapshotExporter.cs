using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace OneNoteMarkdownExporter.Services
{
    public static class InkPageSnapshotExporter
    {
        public static string AppendSnapshotsToMarkdown(
            string markdown,
            string pageXml,
            string pageId,
            string pageName,
            string assetsRoot,
            string relativeAssetsPath,
            IOneNoteService oneNoteService,
            IProgress<string>? progress = null)
        {
            if (!HasInkDrawings(pageXml))
            {
                return markdown;
            }

            try
            {
                var pdfFileName = ExportPathSanitizer.GetSafePageSnapshotFileName(assetsRoot, pageName, "", ".pdf");
                var pdfPath = Path.Combine(assetsRoot, pdfFileName);

                oneNoteService.PublishPageAsPdf(pageId, pdfPath);
                if (!File.Exists(pdfPath))
                {
                    return markdown;
                }

                var relativePath = $"{relativeAssetsPath}/{Path.GetFileName(pdfPath)}".Replace("\\", "/");
                var builder = new StringBuilder();
                builder.Append(markdown.TrimEnd());
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine("## Page snapshot");
                builder.AppendLine();
                builder.AppendLine($"[Open page snapshot PDF]({relativePath})");

                progress?.Report($"  Exported page snapshot PDF for ink: {pageName}");
                return builder.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                progress?.Report($"  Warning: Ink page snapshot export failed for '{pageName}': {ex.Message}");
                return markdown;
            }
        }

        public static bool HasInkDrawings(string pageXml)
        {
            if (!pageXml.Contains("InkDrawing", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                return XDocument.Parse(pageXml)
                    .Descendants()
                    .Any(element => element.Name.LocalName.Equals("InkDrawing", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return true;
            }
        }
    }
}
