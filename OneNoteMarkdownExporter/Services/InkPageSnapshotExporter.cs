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
                var tempXpsPath = Path.Combine(Path.GetTempPath(), $"OneNoteMarkdownExporter_{Guid.NewGuid():N}.xps");
                try
                {
                    oneNoteService.PublishPageAsXps(pageId, tempXpsPath);
                    var svgPaths = XpsToSvgConverter.ConvertToSvgFiles(tempXpsPath, assetsRoot, pageName);
                    if (svgPaths.Count == 0)
                    {
                        return markdown;
                    }

                    var builder = new StringBuilder();
                    builder.Append(markdown.TrimEnd());
                    builder.AppendLine();
                    builder.AppendLine();
                    builder.AppendLine("## Page snapshot");
                    builder.AppendLine();

                    foreach (var svgPath in svgPaths)
                    {
                        var relativePath = $"{relativeAssetsPath}/{Path.GetFileName(svgPath)}".Replace("\\", "/");
                        builder.AppendLine($"![Page snapshot]({relativePath})");
                        builder.AppendLine();
                    }

                    progress?.Report($"  Exported page snapshot SVG for ink: {pageName}");
                    return builder.ToString().TrimEnd();
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempXpsPath))
                        {
                            File.Delete(tempXpsPath);
                        }
                    }
                    catch
                    {
                        // Best-effort cleanup only.
                    }
                }
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
