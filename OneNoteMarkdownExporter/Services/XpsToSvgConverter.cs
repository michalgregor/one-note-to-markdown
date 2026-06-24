using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OneNoteMarkdownExporter.Services
{
    /// <summary>
    /// Converts OneNote-published XPS pages to standalone SVG files. This is intentionally conservative:
    /// it preserves vector paths and basic glyph text, which covers OneNote ink snapshots without rasterizing.
    /// </summary>
    public static class XpsToSvgConverter
    {
        private static readonly XNamespace XpsNs = "http://schemas.microsoft.com/xps/2005/06";

        public static List<string> ConvertToSvgFiles(string xpsPath, string outputFolder, string fileNameStem)
        {
            if (string.IsNullOrWhiteSpace(xpsPath) || !File.Exists(xpsPath))
            {
                throw new FileNotFoundException("XPS file was not created.", xpsPath);
            }

            Directory.CreateDirectory(outputFolder);

            var tempFolder = Path.Combine(Path.GetTempPath(), "OneNoteMarkdownExporter_Xps_" + Guid.NewGuid().ToString("N"));
            try
            {
                ZipFile.ExtractToDirectory(xpsPath, tempFolder);
                var fixedPages = Directory
                    .EnumerateFiles(tempFolder, "*", SearchOption.AllDirectories)
                    .Where(IsFixedPageFile)
                    .Select(path => new { Path = path, Document = TryLoadFixedPage(path) })
                    .Where(item => item.Document?.Root?.Name.LocalName == "FixedPage")
                    .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var result = new List<string>();
                for (var i = 0; i < fixedPages.Count; i++)
                {
                    var suffix = fixedPages.Count == 1 ? "" : $"_{i + 1}";
                    var fileName = ExportPathSanitizer.GetSafePageSnapshotFileName(outputFolder, fileNameStem, suffix, ".svg");
                    var svgPath = Path.Combine(outputFolder, fileName);
                    File.WriteAllText(svgPath, ConvertFixedPageToSvg(fixedPages[i].Document!.Root!), Encoding.UTF8);
                    result.Add(svgPath);
                }

                return result;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempFolder))
                    {
                        Directory.Delete(tempFolder, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
        }

        private static bool IsFixedPageFile(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".fpage", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
        }

        private static XDocument? TryLoadFixedPage(string path)
        {
            try
            {
                return XDocument.Load(path);
            }
            catch
            {
                return null;
            }
        }

        private static string ConvertFixedPageToSvg(XElement fixedPage)
        {
            var width = (string?)fixedPage.Attribute("Width") ?? "0";
            var height = (string?)fixedPage.Attribute("Height") ?? "0";
            var sb = new StringBuilder();

            sb.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{Escape(width)}" height="{Escape(height)}" viewBox="0 0 {Escape(width)} {Escape(height)}">""");
            sb.AppendLine("""  <rect width="100%" height="100%" fill="white"/>""");
            AppendChildren(fixedPage, sb, "  ");
            sb.AppendLine("</svg>");

            return sb.ToString();
        }

        private static void AppendChildren(XElement parent, StringBuilder sb, string indent)
        {
            foreach (var child in parent.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "Canvas":
                        AppendCanvas(child, sb, indent);
                        break;
                    case "Path":
                        AppendPath(child, sb, indent);
                        break;
                    case "Glyphs":
                        AppendGlyphs(child, sb, indent);
                        break;
                }
            }
        }

        private static void AppendCanvas(XElement canvas, StringBuilder sb, string indent)
        {
            var transform = GetTransform(canvas);
            if (string.IsNullOrWhiteSpace(transform))
            {
                AppendChildren(canvas, sb, indent);
                return;
            }

            sb.AppendLine($"""{indent}<g transform="{Escape(transform)}">""");
            AppendChildren(canvas, sb, indent + "  ");
            sb.AppendLine($"{indent}</g>");
        }

        private static void AppendPath(XElement path, StringBuilder sb, string indent)
        {
            var data = NormalizePathData((string?)path.Attribute("Data"));
            if (string.IsNullOrWhiteSpace(data))
            {
                return;
            }

            var color = GetBrushColor(path, "Fill", "Path.Fill") ?? "#FF000000";
            var paint = ConvertXpsColor(color);
            var transform = GetTransform(path);
            var transformAttribute = string.IsNullOrWhiteSpace(transform) ? "" : $" transform=\"{Escape(transform)}\"";
            var opacityAttribute = paint.Opacity >= 0.999 ? "" : $" fill-opacity=\"{paint.Opacity.ToString("0.###", CultureInfo.InvariantCulture)}\"";

            sb.AppendLine($"""{indent}<path d="{Escape(data)}" fill="{paint.Color}"{opacityAttribute}{transformAttribute}/>""");
        }

        private static void AppendGlyphs(XElement glyphs, StringBuilder sb, string indent)
        {
            var text = (string?)glyphs.Attribute("UnicodeString");
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var x = (string?)glyphs.Attribute("OriginX") ?? "0";
            var y = (string?)glyphs.Attribute("OriginY") ?? "0";
            var fontSize = (string?)glyphs.Attribute("FontRenderingEmSize") ?? "12";
            var color = (string?)glyphs.Attribute("Fill") ?? "#FF000000";
            var paint = ConvertXpsColor(color);
            var transform = GetTransform(glyphs);
            var transformAttribute = string.IsNullOrWhiteSpace(transform) ? "" : $" transform=\"{Escape(transform)}\"";
            var opacityAttribute = paint.Opacity >= 0.999 ? "" : $" fill-opacity=\"{paint.Opacity.ToString("0.###", CultureInfo.InvariantCulture)}\"";

            sb.AppendLine($"""{indent}<text x="{Escape(x)}" y="{Escape(y)}" font-size="{Escape(fontSize)}" font-family="Segoe UI, Arial, sans-serif" fill="{paint.Color}"{opacityAttribute}{transformAttribute}>{Escape(text)}</text>""");
        }

        private static string NormalizePathData(string? data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return "";
            }

            // XPS prefixes filled paths with fill-rule tokens such as "F 1"; SVG path data does not.
            return Regex.Replace(data.Trim(), @"^F\s+\d+\s+", "", RegexOptions.IgnoreCase);
        }

        private static string? GetBrushColor(XElement element, string attributeName, string propertyElementName)
        {
            var attribute = (string?)element.Attribute(attributeName);
            if (!string.IsNullOrWhiteSpace(attribute))
            {
                return attribute;
            }

            var propertyElement = element.Elements().FirstOrDefault(child => child.Name.LocalName == propertyElementName);
            var solidBrush = propertyElement?
                .Descendants()
                .FirstOrDefault(child => child.Name.LocalName == "SolidColorBrush");

            return (string?)solidBrush?.Attribute("Color");
        }

        private static string GetTransform(XElement element)
        {
            var transform = (string?)element.Attribute("RenderTransform");
            if (!string.IsNullOrWhiteSpace(transform))
            {
                return MatrixToSvgTransform(transform);
            }

            var propertyElement = element.Elements().FirstOrDefault(child => child.Name.LocalName.EndsWith(".RenderTransform", StringComparison.Ordinal));
            var matrixTransform = propertyElement?
                .Descendants()
                .FirstOrDefault(child => child.Name.LocalName == "MatrixTransform");

            return MatrixToSvgTransform((string?)matrixTransform?.Attribute("Matrix"));
        }

        private static string MatrixToSvgTransform(string? matrix)
        {
            if (string.IsNullOrWhiteSpace(matrix))
            {
                return "";
            }

            var values = matrix.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return values.Length == 6 ? $"matrix({string.Join(" ", values)})" : "";
        }

        private static SvgPaint ConvertXpsColor(string color)
        {
            if (Regex.IsMatch(color, "^#[0-9a-fA-F]{8}$"))
            {
                var alpha = Convert.ToInt32(color.Substring(1, 2), 16);
                return new SvgPaint("#" + color.Substring(3, 6), alpha / 255.0);
            }

            if (Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
            {
                return new SvgPaint(color, 1);
            }

            return new SvgPaint(color, 1);
        }

        private static string Escape(string value)
        {
            return SecurityElement.Escape(value) ?? "";
        }

        private sealed record SvgPaint(string Color, double Opacity);
    }
}
