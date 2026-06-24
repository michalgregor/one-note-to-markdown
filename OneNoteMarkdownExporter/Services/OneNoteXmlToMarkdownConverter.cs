using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlAgilityPack;
using ReverseMarkdown;

namespace OneNoteMarkdownExporter.Services
{
    /// <summary>
    /// Delegate for fetching binary content from OneNote using a callback ID.
    /// </summary>
    /// <param name="callbackId">The callback ID of the binary object.</param>
    /// <returns>Base64-encoded string of the binary content, or null if not available.</returns>
    public delegate string? BinaryContentFetcher(string callbackId);

    /// <summary>
    /// Converts OneNote page XML directly to Markdown without using the Publish API.
    /// This bypasses DLP/sensitivity label restrictions that block the Publish() method.
    /// Uses ReverseMarkdown for proper HTML-to-Markdown conversion.
    /// </summary>
    public class OneNoteXmlToMarkdownConverter : IMarkdownContentConverter
    {
        private readonly XNamespace _ns = "http://schemas.microsoft.com/office/onenote/2013/onenote";

        // Sentinel tokens for inline styles that have no native Markdown form. They are emitted during HTML
        // building, pass through ReverseMarkdown untouched (plain @@..@@ text), and are translated to their
        // final form (Obsidian highlight / inline HTML) in CleanupMarkdown.
        private const string UnderlineOpen = "@@ULON@@";
        private const string UnderlineClose = "@@ULOFF@@";
        private const string SuperscriptOpen = "@@SUPON@@";
        private const string SuperscriptClose = "@@SUPOFF@@";
        private const string SubscriptOpen = "@@SUBON@@";
        private const string SubscriptClose = "@@SUBOFF@@";
        private const string HighlightOpen = "@@HLON:";   // followed by <colorkey>@@
        private const string HighlightClose = "@@HLOFF@@";
        private const string FontColorOpen = "@@FCON:";   // followed by <colorkey>@@
        private const string FontColorClose = "@@FCOFF@@";
        private const string TodoUnchecked = "@@TODO0@@";
        private const string TodoChecked = "@@TODO1@@";
        // Line break inside a table cell - kept as <br> (which Markdown tables allow) instead of a real
        // newline, which would split the table row.
        private const string CellBreak = "@@CELLBR@@";
        // Image width (px) carried in the <img> alt text so it survives ReverseMarkdown, then turned into
        // the Obsidian ![alt|width](src) syntax.
        private const string ImageWidthOpen = "@@IMGW:";
        // LaTeX (base64) for inline / block math, decoded into $..$ / $$..$$ in CleanupMarkdown.
        private const string MathInlineOpen = "@@MATH:";
        private const string MathBlockOpen = "@@MATHB:";

        private static readonly XNamespace MathNs = "http://www.w3.org/1998/Math/MathML";

        // OneNote "To Do" tag definitions use symbol="3".
        private const string TodoTagSymbol = "3";

        // Background-color keys treated as the "default" highlight (rendered as Obsidian ==text==).
        private static readonly HashSet<string> DefaultHighlightColors = new(StringComparer.OrdinalIgnoreCase)
        {
            "yellow", "ffff00", "ffff66", "ffff99", "fefb00", "fff200", "fffc00"
        };
        private readonly Converter _markdownConverter;
        private string _assetsFolder = "";
        private string _relativeAssetsPath = "";
        private string _pagePrefix = "";
        private int _imageCounter = 0;
        private int _attachmentCounter = 0;
        private BinaryContentFetcher? _binaryContentFetcher;

        /// <summary>
        /// When true, font (foreground) colors are preserved as inline <c>&lt;span style="color:..."&gt;</c>.
        /// Off by default because colored text is often noise in exported notes.
        /// </summary>
        public bool IncludeFontColors { get; set; } = false;

        // Per-page lookup tables built from the page's QuickStyleDef / TagDef declarations.
        private Dictionary<int, string> _quickStyles = new();
        private HashSet<int> _todoTagIndexes = new();

        public OneNoteXmlToMarkdownConverter()
        {
            var config = new ReverseMarkdown.Config
            {
                UnknownTags = Config.UnknownTagsOption.Drop,
                GithubFlavored = true,
                RemoveComments = true,
                SmartHrefHandling = true
            };
            _markdownConverter = new Converter(config);
        }

        public string Convert(string pageXml, string assetsFolder, string relativeAssetsPath, BinaryContentFetcher? binaryContentFetcher = null, string? pagePrefix = null)
        {
            _assetsFolder = assetsFolder;
            _relativeAssetsPath = relativeAssetsPath;
            _pagePrefix = SanitizePrefix(pagePrefix);
            _imageCounter = 0;
            _attachmentCounter = 0;
            _binaryContentFetcher = binaryContentFetcher;

            var doc = XDocument.Parse(pageXml);
            if (doc.Root == null) return "";

            _quickStyles = BuildQuickStyleMap(doc.Root);
            _todoTagIndexes = BuildTodoTagIndexes(doc.Root);

            // Build HTML first, then convert to clean Markdown using ReverseMarkdown
            var htmlBuilder = new StringBuilder();
            htmlBuilder.AppendLine("<html><body>");

            // Get page title
            var titleElement = doc.Root.Element(_ns + "Title");
            if (titleElement != null)
            {
                var titleText = GetPlainText(titleElement.Element(_ns + "OE"));
                if (!string.IsNullOrWhiteSpace(titleText))
                {
                    htmlBuilder.AppendLine($"<h1>{System.Net.WebUtility.HtmlEncode(titleText.Trim())}</h1>");
                }
            }

            // Process all Outline elements (main content containers)
            foreach (var outline in doc.Root.Elements(_ns + "Outline"))
            {
                ProcessOutline(outline, htmlBuilder);
            }

            // Process any images directly on the page (outside outlines)
            foreach (var image in doc.Root.Elements(_ns + "Image"))
            {
                ProcessImage(image, htmlBuilder);
            }

            // Process any file attachments directly on the page (outside outlines)
            foreach (var file in doc.Root.Elements(_ns + "InsertedFile").Concat(doc.Root.Elements(_ns + "MediaFile")))
            {
                htmlBuilder.Append(ProcessAttachmentToHtml(file));
            }

            htmlBuilder.AppendLine("</body></html>");

            // Get the HTML and normalize anchor tags BEFORE ReverseMarkdown processing
            var html = htmlBuilder.ToString();
            html = NormalizeHtmlAnchors(html);

            // Convert HTML to Markdown using ReverseMarkdown library
            var markdown = _markdownConverter.Convert(html);

            // Final cleanup
            markdown = CleanupMarkdown(markdown);

            return markdown;
        }

        private Dictionary<int, string> BuildQuickStyleMap(XElement root)
        {
            var map = new Dictionary<int, string>();
            foreach (var def in root.Elements(_ns + "QuickStyleDef"))
            {
                var name = def.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(name) && int.TryParse(def.Attribute("index")?.Value, out var index))
                {
                    map[index] = name!;
                }
            }
            return map;
        }

        private HashSet<int> BuildTodoTagIndexes(XElement root)
        {
            var set = new HashSet<int>();
            foreach (var def in root.Elements(_ns + "TagDef"))
            {
                if (def.Attribute("symbol")?.Value == TodoTagSymbol && int.TryParse(def.Attribute("index")?.Value, out var index))
                {
                    set.Add(index);
                }
            }
            return set;
        }

        /// <summary>Returns "h1".."h6" if the OE uses a OneNote heading quick style, otherwise null.</summary>
        private string? GetHeadingTag(XElement oe)
        {
            if (int.TryParse(oe.Attribute("quickStyleIndex")?.Value, out var index)
                && _quickStyles.TryGetValue(index, out var name))
            {
                var match = Regex.Match(name, @"^h([1-6])$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return "h" + match.Groups[1].Value;
                }
            }
            return null;
        }

        /// <summary>Detects a OneNote "To Do" tag on the OE, returning whether it exists and is completed.</summary>
        private (bool isTodo, bool completed) GetTodoState(XElement oe)
        {
            foreach (var tag in oe.Elements(_ns + "Tag"))
            {
                if (int.TryParse(tag.Attribute("index")?.Value, out var index) && _todoTagIndexes.Contains(index))
                {
                    return (true, tag.Attribute("completed")?.Value == "true");
                }
            }
            return (false, false);
        }

        private static string SanitizePrefix(string? prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return "";
            }

            return ExportPathSanitizer.SanitizeComponent(prefix, "page", prefix).Replace(' ', '_');
        }

        /// <summary>
        /// Normalizes HTML anchor tags to ensure they're on single lines
        /// so ReverseMarkdown can process them correctly.
        /// </summary>
        private string NormalizeHtmlAnchors(string html)
        {
            // Find all <a ...>...</a> tags and normalize them to single lines
            // This regex captures the entire anchor tag including content
            html = Regex.Replace(html,
                @"<a\s([^>]*?)>",
                match => {
                    // Normalize whitespace in the opening tag
                    var attributes = match.Groups[1].Value;
                    attributes = Regex.Replace(attributes, @"\s+", " ").Trim();
                    return $"<a {attributes}>";
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return html;
        }

        private void ProcessOutline(XElement outline, StringBuilder html)
        {
            var oeChildren = outline.Element(_ns + "OEChildren");
            if (oeChildren != null)
            {
                ProcessOEChildren(oeChildren, html);
            }
        }

        private void ProcessOEChildren(XElement oeChildren, StringBuilder html)
        {
            // Check if this is a list context
            var elements = oeChildren.Elements(_ns + "OE").ToList();
            
            bool inBulletList = false;
            bool inNumberedList = false;

            foreach (var oe in elements)
            {
                var listElement = oe.Element(_ns + "List");
                bool isBullet = listElement?.Element(_ns + "Bullet") != null;
                bool isNumbered = listElement?.Element(_ns + "Number") != null;

                // Check if this OE has any real content (not just whitespace)
                bool hasContent = HasRealContent(oe);

                // Handle list transitions
                if (isBullet && !inBulletList)
                {
                    if (inNumberedList) { html.AppendLine("</ol>"); inNumberedList = false; }
                    html.AppendLine("<ul>");
                    inBulletList = true;
                }
                else if (isNumbered && !inNumberedList)
                {
                    if (inBulletList) { html.AppendLine("</ul>"); inBulletList = false; }
                    html.AppendLine("<ol>");
                    inNumberedList = true;
                }
                else if (!isBullet && !isNumbered && (inBulletList || inNumberedList))
                {
                    // Only close the list if this is a non-empty paragraph
                    // Empty paragraphs (blank lines) should not break list continuity
                    if (hasContent)
                    {
                        if (inBulletList) { html.AppendLine("</ul>"); inBulletList = false; }
                        if (inNumberedList) { html.AppendLine("</ol>"); inNumberedList = false; }
                    }
                    // If no content, just skip - don't close the list
                }

                // Only process if it has content or is a list item
                if (hasContent || isBullet || isNumbered)
                {
                    ProcessOE(oe, html, inBulletList || inNumberedList);
                }
            }

            // Close any open lists
            if (inBulletList) html.AppendLine("</ul>");
            if (inNumberedList) html.AppendLine("</ol>");
        }

        /// <summary>
        /// Checks if an OE element has any real text content (not just whitespace or empty elements)
        /// </summary>
        private bool HasRealContent(XElement oe)
        {
            // Check text elements
            foreach (var t in oe.Elements(_ns + "T"))
            {
                var cdata = t.Nodes().OfType<XCData>().FirstOrDefault();
                var text = cdata?.Value ?? t.Value;
                // Strip HTML tags and check if there's real content
                text = Regex.Replace(text, "<[^>]+>", "");
                if (!string.IsNullOrWhiteSpace(text))
                    return true;
            }
            
            // Check for images
            if (oe.Elements(_ns + "Image").Any())
                return true;

            // Check for file attachments / embedded media
            if (oe.Elements(_ns + "InsertedFile").Any() || oe.Elements(_ns + "MediaFile").Any())
                return true;

            // Check for tables
            if (oe.Elements(_ns + "Table").Any())
                return true;
            
            // Check nested children
            var nestedChildren = oe.Element(_ns + "OEChildren");
            if (nestedChildren != null && nestedChildren.Elements(_ns + "OE").Any())
            {
                foreach (var child in nestedChildren.Elements(_ns + "OE"))
                {
                    if (HasRealContent(child))
                        return true;
                }
            }

            return false;
        }

        private void ProcessOE(XElement oe, StringBuilder html, bool inList)
        {
            var listElement = oe.Element(_ns + "List");
            bool isListItem = listElement != null && 
                (listElement.Element(_ns + "Bullet") != null || listElement.Element(_ns + "Number") != null);

            // Build content for this element
            var content = new StringBuilder();

            // Process text elements
            foreach (var t in oe.Elements(_ns + "T"))
            {
                content.Append(ProcessTextElement(t));
            }

            // Process tables
            foreach (var table in oe.Elements(_ns + "Table"))
            {
                content.Append(ProcessTable(table));
            }

            // Process images
            foreach (var image in oe.Elements(_ns + "Image"))
            {
                content.Append(ProcessImageToHtml(image));
            }

            // Process file attachments (PDFs, Office docs, etc.) and embedded media files
            foreach (var file in oe.Elements(_ns + "InsertedFile").Concat(oe.Elements(_ns + "MediaFile")))
            {
                content.Append(ProcessAttachmentToHtml(file));
            }

            var textContent = content.ToString();
            bool hasContent = !string.IsNullOrWhiteSpace(Regex.Replace(textContent, "<[^>]*>", "").Trim());

            if (hasContent || content.Length > 0)
            {
                var (isTodo, todoChecked) = GetTodoState(oe);
                var headingTag = GetHeadingTag(oe);

                if (isListItem || inList)
                {
                    html.Append("<li>");
                    html.Append(textContent);
                }
                else if (isTodo)
                {
                    html.Append("<p>");
                    html.Append(todoChecked ? TodoChecked : TodoUnchecked);
                    html.Append(textContent);
                    html.AppendLine("</p>");
                }
                else if (headingTag != null)
                {
                    html.Append($"<{headingTag}>");
                    html.Append(textContent);
                    html.AppendLine($"</{headingTag}>");
                }
                else
                {
                    html.Append("<p>");
                    html.Append(textContent);
                    html.AppendLine("</p>");
                }
            }

            // Process nested children
            var nestedChildren = oe.Element(_ns + "OEChildren");
            if (nestedChildren != null)
            {
                if (isListItem || inList)
                {
                    // Nested content within list item
                    ProcessOEChildren(nestedChildren, html);
                }
                else
                {
                    ProcessOEChildren(nestedChildren, html);
                }
            }

            if ((hasContent || content.Length > 0) && (isListItem || inList))
            {
                html.AppendLine("</li>");
            }
        }

        private string ProcessTextElement(XElement t)
        {
            // Get raw text - may be in CDATA or direct
            string rawText = "";
            
            var cdata = t.Nodes().OfType<XCData>().FirstOrDefault();
            if (cdata != null)
            {
                rawText = cdata.Value;
            }
            else
            {
                rawText = t.Value;
            }

            if (string.IsNullOrEmpty(rawText)) return "";

            // Convert any embedded MathML equations to LaTeX sentinels before other processing.
            if (rawText.Contains("[if mathML]"))
            {
                rawText = ConvertMathComments(rawText);
            }

            // Always normalize anchor tags first, regardless of other HTML detection
            // This catches <a\nhref=... patterns where the tag spans lines
            if (rawText.Contains("<a"))
            {
                // Normalize anchor tags - collapse any whitespace after <a to single space
                rawText = Regex.Replace(rawText,
                    @"<a\s+",
                    "<a ",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }

            // If text contains HTML (from OneNote's rich text), pass it through
            // ReverseMarkdown will handle the conversion
            if (rawText.Contains("<span") || rawText.Contains("<a ") || rawText.Contains("<b>") || rawText.Contains("<i>"))
            {
                // Clean up OneNote's span styles to simpler HTML
                rawText = ConvertOneNoteStylesToHtml(rawText);
                return rawText;
            }

            // Apply inline styles from the T element's style attribute.
            var style = t.Attribute("style")?.Value ?? "";

            // Escape plain text for HTML, then wrap according to the style.
            var escaped = System.Net.WebUtility.HtmlEncode(rawText);
            return WrapByStyle(style, escaped);
        }
        private static readonly Regex InnerSpanRegex = new Regex(
            @"<span\b([^>]*)>((?:(?!</?span\b)[\s\S])*?)</span>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private string ConvertOneNoteStylesToHtml(string html)
        {
            // Convert OneNote's inline <span style='...'> formatting into Markdown-friendly markup.
            // Spans are processed innermost-first so nested styles compose correctly; spans whose styles
            // we don't recognize are unwrapped (tag removed, content kept).
            string previous;
            var guard = 0;
            do
            {
                previous = html;
                html = InnerSpanRegex.Replace(html, match =>
                    WrapByStyle(GetStyleAttribute(match.Groups[1].Value), match.Groups[2].Value));
                guard++;
            }
            while (html != previous && guard < 50);

            // Drop any spans we couldn't pair up, and clean up Office-specific mso- styles.
            html = Regex.Replace(html, @"</?span[^>]*>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"mso-[^;""']+[;]?", "");

            return html;
        }

        private static string GetStyleAttribute(string attributes)
        {
            var match = Regex.Match(attributes, @"style\s*=\s*['""]([^'""]*)['""]", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "";
        }

        /// <summary>
        /// Wraps already-escaped content according to the CSS-like style string from a OneNote span or
        /// T element. Bold/italic/strikethrough become standard HTML tags (ReverseMarkdown converts them);
        /// underline, super/subscript, highlight, and font color use sentinel tokens that survive
        /// ReverseMarkdown and are translated in <see cref="CleanupMarkdown"/>.
        /// </summary>
        private string WrapByStyle(string? style, string content)
        {
            if (string.IsNullOrEmpty(content) || string.IsNullOrWhiteSpace(style))
            {
                return content;
            }

            // Strikethrough / bold / italic -> native tags (innermost).
            if (Regex.IsMatch(style, @"text-decoration:[^;'""]*line-through", RegexOptions.IgnoreCase))
            {
                content = $"<del>{content}</del>";
            }
            if (Regex.IsMatch(style, @"font-weight:\s*(bold|[6-9]00)", RegexOptions.IgnoreCase))
            {
                content = $"<strong>{content}</strong>";
            }
            if (Regex.IsMatch(style, @"font-style:\s*italic", RegexOptions.IgnoreCase))
            {
                content = $"<em>{content}</em>";
            }
            if (Regex.IsMatch(style, @"text-decoration:[^;'""]*underline", RegexOptions.IgnoreCase))
            {
                content = $"{UnderlineOpen}{content}{UnderlineClose}";
            }

            // Superscript / subscript.
            if (Regex.IsMatch(style, @"vertical-align:\s*super", RegexOptions.IgnoreCase))
            {
                content = $"{SuperscriptOpen}{content}{SuperscriptClose}";
            }
            else if (Regex.IsMatch(style, @"vertical-align:\s*sub", RegexOptions.IgnoreCase))
            {
                content = $"{SubscriptOpen}{content}{SubscriptClose}";
            }

            // Font (foreground) color - opt-in. Avoid matching "background-color".
            if (IncludeFontColors)
            {
                var colorMatch = Regex.Match(style, @"(?<![-\w])color:\s*(#?[0-9a-zA-Z]+)", RegexOptions.IgnoreCase);
                if (colorMatch.Success)
                {
                    content = $"{FontColorOpen}{NormalizeColor(colorMatch.Groups[1].Value)}@@{content}{FontColorClose}";
                }
            }

            // Highlight (background) - outermost.
            var backgroundMatch = Regex.Match(style, @"background(?:-color)?:\s*(#?[0-9a-zA-Z]+)", RegexOptions.IgnoreCase);
            if (backgroundMatch.Success)
            {
                content = $"{HighlightOpen}{NormalizeColor(backgroundMatch.Groups[1].Value)}@@{content}{HighlightClose}";
            }

            return content;
        }

        private static string NormalizeColor(string color)
        {
            // Strip a leading '#' and lowercase, leaving only [0-9a-z] so it survives inside a sentinel.
            return color.TrimStart('#').ToLowerInvariant();
        }

        /// <summary>
        /// Replaces OneNote's MathML equations (stored as <c>&lt;!--[if mathML]&gt;&lt;math&gt;…&lt;/math&gt;&lt;![endif]--&gt;</c>
        /// conditional comments) with base64-encoded LaTeX sentinels. The sentinels survive ReverseMarkdown
        /// untouched and become <c>$…$</c> / <c>$$…$$</c> in <see cref="ResolveFormattingSentinels"/>.
        /// </summary>
        private string ConvertMathComments(string text)
        {
            return Regex.Replace(text, @"<!--\[if mathML\]>(.*?)<!\[endif\]-->", match =>
            {
                var mathml = match.Groups[1].Value;
                // Prefer the robust xsltml library; fall back to the lightweight built-in converter.
                var latex = MathmlToLatexConverter.TryConvert(mathml, out var xsltLatex)
                    ? xsltLatex
                    : MathmlToLatex(mathml);
                if (string.IsNullOrWhiteSpace(latex)) return "";

                var encoded = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(latex));
                var isBlock = Regex.IsMatch(mathml, @"display\s*=\s*[""']block[""']", RegexOptions.IgnoreCase);
                return $"{(isBlock ? MathBlockOpen : MathInlineOpen)}{encoded}@@";
            }, RegexOptions.Singleline);
        }

        private static string MathmlToLatex(string mathmlXml)
        {
            try
            {
                return MathNode(XElement.Parse(mathmlXml)).Trim();
            }
            catch
            {
                return "";
            }
        }

        private static string MathNode(XElement element)
        {
            switch (element.Name.LocalName)
            {
                case "math":
                case "mrow":
                case "mstyle":
                case "mpadded":
                case "semantics":
                    return ConcatMath(element);
                case "mi":
                    return MapMathIdentifier(element.Value);
                case "mn":
                    return element.Value;
                case "mo":
                    return MapMathOperator(element.Value);
                case "mtext":
                    return string.IsNullOrEmpty(element.Value) ? "" : $"\\text{{{element.Value}}}";
                case "msup":
                    return $"{MathBase(element, 0)}^{{{MathChild(element, 1)}}}";
                case "msub":
                    return $"{MathBase(element, 0)}_{{{MathChild(element, 1)}}}";
                case "msubsup":
                    return $"{MathBase(element, 0)}_{{{MathChild(element, 1)}}}^{{{MathChild(element, 2)}}}";
                case "mfrac":
                    return $"\\frac{{{MathChild(element, 0)}}}{{{MathChild(element, 1)}}}";
                case "msqrt":
                    return $"\\sqrt{{{ConcatMath(element)}}}";
                case "mroot":
                    return $"\\sqrt[{MathChild(element, 1)}]{{{MathChild(element, 0)}}}";
                case "mfenced":
                    var open = element.Attribute("open")?.Value ?? "(";
                    var close = element.Attribute("close")?.Value ?? ")";
                    return open + ConcatMath(element) + close;
                case "mover":
                    return $"\\overline{{{MathChild(element, 0)}}}";
                default:
                    return ConcatMath(element);
            }
        }

        private static string ConcatMath(XElement element)
        {
            return string.Concat(element.Elements().Select(MathNode));
        }

        private static string MathChild(XElement element, int index)
        {
            var child = element.Elements().ElementAtOrDefault(index);
            return child != null ? MathNode(child) : "";
        }

        /// <summary>Renders the base of a sub/superscript, adding braces only when it's more than one token.</summary>
        private static string MathBase(XElement element, int index)
        {
            var rendered = MathChild(element, index);
            return rendered.Length <= 1 ? rendered : $"{{{rendered}}}";
        }

        private static string MapMathIdentifier(string value)
        {
            return value switch
            {
                "α" => "\\alpha ", "β" => "\\beta ", "γ" => "\\gamma ", "δ" => "\\delta ",
                "ε" => "\\epsilon ", "θ" => "\\theta ", "λ" => "\\lambda ", "μ" => "\\mu ",
                "π" => "\\pi ", "ρ" => "\\rho ", "σ" => "\\sigma ", "τ" => "\\tau ",
                "φ" => "\\phi ", "ψ" => "\\psi ", "ω" => "\\omega ",
                "Δ" => "\\Delta ", "Σ" => "\\Sigma ", "Π" => "\\Pi ", "Ω" => "\\Omega ",
                "∅" => "\\emptyset ",
                _ => value
            };
        }

        private static string MapMathOperator(string value)
        {
            return value.Trim() switch
            {
                "−" => "-",
                "×" => " \\times ",
                "·" or "⋅" => " \\cdot ",
                "±" => " \\pm ",
                "≤" => " \\le ",
                "≥" => " \\ge ",
                "≠" => " \\ne ",
                "≈" => " \\approx ",
                "→" => " \\to ",
                "⇒" => " \\Rightarrow ",
                "∞" => "\\infty ",
                "∈" => " \\in ",
                "∉" => " \\notin ",
                "∑" => "\\sum ",
                "∏" => "\\prod ",
                "∫" => "\\int ",
                "∂" => "\\partial ",
                "∇" => "\\nabla ",
                "∅" => "\\emptyset ",
                "∪" => " \\cup ",
                "∩" => " \\cap ",
                "⊆" => " \\subseteq ",
                "⊂" => " \\subset ",
                "\\" => "\\backslash ",
                _ => value
            };
        }

        private void ProcessImage(XElement image, StringBuilder html)
        {
            html.Append(ProcessImageToHtml(image));
        }

        /// <summary>Reads the image's display width (rounded to whole pixels) from its Size element, if set.</summary>
        private int? GetImageWidthPx(XElement image)
        {
            var width = image.Element(_ns + "Size")?.Attribute("width")?.Value;
            if (!string.IsNullOrEmpty(width)
                && double.TryParse(width, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var px)
                && px >= 1)
            {
                return (int)Math.Round(px);
            }
            return null;
        }

        private string ProcessImageToHtml(XElement image)
        {
            try
            {
                string? base64Data = null;
                
                // First, try to get embedded data from the Data element
                var dataElement = image.Element(_ns + "Data");
                if (dataElement != null && !string.IsNullOrWhiteSpace(dataElement.Value))
                {
                    base64Data = dataElement.Value.Trim();
                }
                
                // If no embedded data, try to fetch using callbackID
                if (string.IsNullOrWhiteSpace(base64Data))
                {
                    var callbackId = image.Attribute("callbackID")?.Value;
                    if (!string.IsNullOrWhiteSpace(callbackId) && _binaryContentFetcher != null)
                    {
                        base64Data = _binaryContentFetcher(callbackId);
                    }
                }
                
                // If we still don't have data, return a placeholder
                if (string.IsNullOrWhiteSpace(base64Data))
                {
                    // Log additional info for debugging
                    var callbackId = image.Attribute("callbackID")?.Value;
                    var objectId = image.Attribute("objectID")?.Value;
                    var info = $"callbackID={callbackId ?? "none"}, objectID={objectId ?? "none"}";
                    return $"<p><em>[Image - no embedded data, could not fetch binary content. {System.Net.WebUtility.HtmlEncode(info)}]</em></p>";
                }
                
                // Remove any whitespace from base64
                base64Data = Regex.Replace(base64Data, @"\s+", "");

                // Determine format
                var format = image.Attribute("format")?.Value?.ToLower() ?? "png";
                var extension = format switch
                {
                    "png" => ".png",
                    "jpg" or "jpeg" => ".jpg",
                    "gif" => ".gif",
                    "bmp" => ".bmp",
                    "emf" => ".png", // Convert EMF reference to PNG
                    "wmf" => ".png", // Convert WMF reference to PNG
                    _ => ".png"
                };

                // Generate unique filename with page prefix to avoid collisions across pages
                _imageCounter++;
                var fileName = ExportPathSanitizer.GetSafeAssetFileName(_assetsFolder, _pagePrefix, _imageCounter, extension);
                var filePath = Path.Combine(_assetsFolder, fileName);

                // Ensure assets folder exists
                if (!Directory.Exists(_assetsFolder))
                {
                    Directory.CreateDirectory(_assetsFolder);
                }

                // Decode and save
                var imageBytes = System.Convert.FromBase64String(base64Data);
                File.WriteAllBytes(filePath, imageBytes);

                // Return HTML img tag, encoding the original width (if any) into the alt text so it can be
                // turned into Obsidian's ![alt|width](src) sizing syntax after ReverseMarkdown.
                var relativePath = $"{_relativeAssetsPath}/{fileName}".Replace("\\", "/");
                var widthPx = GetImageWidthPx(image);
                var alt = widthPx.HasValue ? $"image{ImageWidthOpen}{widthPx.Value}@@" : "image";
                return $"<p><img src=\"{relativePath}\" alt=\"{alt}\" /></p>";
            }
            catch (Exception ex)
            {
                return $"<p><em>[Image export failed: {System.Net.WebUtility.HtmlEncode(ex.Message)}]</em></p>";
            }
        }

        /// <summary>
        /// Converts a OneNote InsertedFile/MediaFile element (PDFs, Office documents, audio/video, etc.)
        /// into an HTML link, copying the cached binary out of OneNote's local cache into the assets folder.
        /// When the binary has not been downloaded locally, a visible placeholder is emitted instead of
        /// silently dropping the attachment.
        /// </summary>
        private string ProcessAttachmentToHtml(XElement file)
        {
            var preferredName = file.Attribute("preferredName")?.Value;
            var pathCache = file.Attribute("pathCache")?.Value;
            var pathSource = file.Attribute("pathSource")?.Value;

            // The display name; fall back to the source file name when preferredName is missing.
            var displayName = !string.IsNullOrWhiteSpace(preferredName)
                ? preferredName!
                : (!string.IsNullOrWhiteSpace(pathSource) ? Path.GetFileName(pathSource) : "attachment");

            try
            {
                // The binary lives at pathCache (OneNote's OneNoteOfflineCache_Files). If it is missing or the
                // file is not on disk yet, OneNote has not made it available for export.
                if (string.IsNullOrWhiteSpace(pathCache) || !File.Exists(pathCache))
                {
                    var reason = string.IsNullOrWhiteSpace(pathCache)
                        ? "no local cache path"
                        : "not downloaded locally";
                    return $"<p><em>[Attachment unavailable: {System.Net.WebUtility.HtmlEncode(displayName)} ({reason})]</em></p>";
                }

                // Determine the real extension. preferredName/pathSource carry the original name
                // (e.g. "1711.05101.pdf"); pathCache is always OneNote's ".bin" cache file, so it is only
                // used to sniff the content type when the names don't give a usable extension.
                var extension = GetAttachmentExtension(preferredName, pathSource, pathCache);

                _attachmentCounter++;
                var fileName = ExportPathSanitizer.GetSafeAttachmentFileName(_assetsFolder, _pagePrefix, displayName, _attachmentCounter, extension);
                var filePath = Path.Combine(_assetsFolder, fileName);

                if (!Directory.Exists(_assetsFolder))
                {
                    Directory.CreateDirectory(_assetsFolder);
                }

                File.Copy(pathCache, filePath, overwrite: true);

                var relativePath = $"{_relativeAssetsPath}/{fileName}".Replace("\\", "/");
                return $"<p>\U0001F4CE <a href=\"{relativePath}\">{System.Net.WebUtility.HtmlEncode(displayName)}</a></p>";
            }
            catch (Exception ex)
            {
                return $"<p><em>[Attachment export failed: {System.Net.WebUtility.HtmlEncode(displayName)} - {System.Net.WebUtility.HtmlEncode(ex.Message)}]</em></p>";
            }
        }

        /// <summary>
        /// Determines an attachment's file extension. Prefers the original name (preferredName, then
        /// pathSource), accepting only plausible extensions (so e.g. an arXiv id like "1711.05101" with no
        /// real extension isn't mistaken for one). Falls back to sniffing the cached file's magic bytes.
        /// </summary>
        private static string GetAttachmentExtension(string? preferredName, string? pathSource, string? cachePath)
        {
            foreach (var name in new[] { preferredName, pathSource })
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var ext = Path.GetExtension(name);
                if (IsLikelyExtension(ext)) return ext.ToLowerInvariant();
            }

            return SniffFileExtension(cachePath);
        }

        private static bool IsLikelyExtension(string ext)
        {
            // A real extension is short and contains at least one letter (rejects ".05101", "", etc.).
            return Regex.IsMatch(ext, @"^\.[A-Za-z0-9]{1,8}$") && ext.Any(char.IsLetter);
        }

        /// <summary>Guesses a file extension from the first bytes of the cached file (magic numbers).</summary>
        private static string SniffFileExtension(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return ".bin";

                var buffer = new byte[16];
                int read;
                using (var stream = File.OpenRead(path))
                {
                    read = stream.Read(buffer, 0, buffer.Length);
                }

                bool Starts(params byte[] sig) => read >= sig.Length && buffer.Take(sig.Length).SequenceEqual(sig);

                if (Starts(0x25, 0x50, 0x44, 0x46)) return ".pdf";                 // %PDF
                if (Starts(0x50, 0x4B, 0x03, 0x04)) return ".zip";                 // PK.. (also docx/xlsx/pptx)
                if (Starts(0x89, 0x50, 0x4E, 0x47)) return ".png";                 // .PNG
                if (Starts(0xFF, 0xD8, 0xFF)) return ".jpg";                       // JPEG
                if (Starts(0x47, 0x49, 0x46, 0x38)) return ".gif";                 // GIF8
                if (Starts(0xD0, 0xCF, 0x11, 0xE0)) return ".doc";                 // legacy OLE (doc/xls/ppt)

                var firstNonSpace = buffer.Take(read).FirstOrDefault(b => b != 0x20 && b != 0x09 && b != 0x0A && b != 0x0D && b != 0xEF && b != 0xBB && b != 0xBF);
                if (firstNonSpace == (byte)'{' || firstNonSpace == (byte)'[') return ".json";
                if (read > 0 && buffer.Take(read).All(b => b == 0x09 || b == 0x0A || b == 0x0D || (b >= 0x20 && b < 0x7F))) return ".txt";

                return ".bin";
            }
            catch
            {
                return ".bin";
            }
        }

        private string ProcessTable(XElement table)
        {
            var rows = table.Elements(_ns + "Row").ToList();
            if (!rows.Any()) return "";

            var sb = new StringBuilder();
            sb.AppendLine("<table>");

            bool isFirstRow = true;
            foreach (var row in rows)
            {
                sb.AppendLine("<tr>");
                foreach (var cell in row.Elements(_ns + "Cell"))
                {
                    var tag = isFirstRow ? "th" : "td";
                    var cellContent = GetCellContent(cell);
                    sb.AppendLine($"<{tag}>{cellContent}</{tag}>");
                }
                sb.AppendLine("</tr>");
                isFirstRow = false;
            }

            sb.AppendLine("</table>");
            return sb.ToString();
        }

        private string GetCellContent(XElement cell)
        {
            var oeChildren = cell.Element(_ns + "OEChildren");
            if (oeChildren == null) return "";

            var parts = new List<string>();
            foreach (var oe in oeChildren.Elements(_ns + "OE"))
            {
                var text = new StringBuilder();
                foreach (var t in oe.Elements(_ns + "T"))
                {
                    text.Append(ProcessTextElement(t));
                }
                if (text.Length > 0)
                {
                    parts.Add(text.ToString());
                }
            }

            // Join multi-paragraph cells with a sentinel (not a real <br>, which the global cleanup would
            // turn into a newline and break the table). It becomes <br> again at the end of CleanupMarkdown.
            var joined = string.Join(CellBreak, parts);
            joined = Regex.Replace(joined, @"<br[^>]*/?>", CellBreak, RegexOptions.IgnoreCase);
            return joined;
        }

        private string GetPlainText(XElement? oe)
        {
            if (oe == null) return "";

            var sb = new StringBuilder();
            foreach (var t in oe.Elements(_ns + "T"))
            {
                var cdata = t.Nodes().OfType<XCData>().FirstOrDefault();
                var text = cdata?.Value ?? t.Value;
                
                // Strip HTML tags
                text = Regex.Replace(text, "<[^>]+>", "");
                sb.Append(text);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Translates the inline-style sentinel tokens emitted by <see cref="WrapByStyle"/> into their final
        /// representation: Obsidian highlights (<c>==text==</c>) for the default highlight color, and inline
        /// HTML (<c>&lt;mark&gt;</c>, <c>&lt;u&gt;</c>, <c>&lt;sup&gt;</c>, <c>&lt;sub&gt;</c>,
        /// <c>&lt;span style="color:..."&gt;</c>) for everything else - all of which Obsidian renders.
        /// </summary>
        private string ResolveFormattingSentinels(string markdown)
        {
            // To-do paragraphs -> Markdown task list items (each sentinel sits at the start of its line).
            markdown = Regex.Replace(markdown, @"(?m)^([ \t>]*)@@TODO1@@[ ]?", "$1- [x] ");
            markdown = Regex.Replace(markdown, @"(?m)^([ \t>]*)@@TODO0@@[ ]?", "$1- [ ] ");
            // Safety net for any not at a line start (shouldn't normally happen).
            markdown = markdown.Replace(TodoChecked, "- [x] ").Replace(TodoUnchecked, "- [ ] ");

            // Image width carried in the alt text -> Obsidian ![image|width](src) sizing.
            markdown = Regex.Replace(markdown, @"!\[image@@IMGW:(\d+)@@\]", "![image|$1]");

            // Math: base64-encoded LaTeX -> $$..$$ (block) / $..$ (inline).
            markdown = Regex.Replace(markdown, @"@@MATHB:([A-Za-z0-9+/=]+)@@", m => $"$${DecodeBase64(m.Groups[1].Value)}$$");
            markdown = Regex.Replace(markdown, @"@@MATH:([A-Za-z0-9+/=]+)@@", m => $"${DecodeBase64(m.Groups[1].Value)}$");

            markdown = Regex.Replace(markdown, @"@@ULON@@(.*?)@@ULOFF@@", "<u>$1</u>", RegexOptions.Singleline);
            markdown = Regex.Replace(markdown, @"@@SUPON@@(.*?)@@SUPOFF@@", "<sup>$1</sup>", RegexOptions.Singleline);
            markdown = Regex.Replace(markdown, @"@@SUBON@@(.*?)@@SUBOFF@@", "<sub>$1</sub>", RegexOptions.Singleline);

            markdown = Regex.Replace(markdown, @"@@FCON:([0-9a-z]*)@@(.*?)@@FCOFF@@",
                match => $"<span style=\"color:{ToCssColor(match.Groups[1].Value)}\">{match.Groups[2].Value}</span>",
                RegexOptions.Singleline);

            markdown = Regex.Replace(markdown, @"@@HLON:([0-9a-z]*)@@(.*?)@@HLOFF@@",
                match =>
                {
                    var color = match.Groups[1].Value;
                    var inner = match.Groups[2].Value;
                    return DefaultHighlightColors.Contains(color)
                        ? $"=={inner}=="
                        : $"<mark style=\"background:{ToCssColor(color)}\">{inner}</mark>";
                },
                RegexOptions.Singleline);

            return markdown;
        }

        private static string DecodeBase64(string encoded)
        {
            try
            {
                return Encoding.UTF8.GetString(System.Convert.FromBase64String(encoded));
            }
            catch
            {
                return "";
            }
        }

        private static string ToCssColor(string colorKey)
        {
            if (string.IsNullOrEmpty(colorKey)) return "yellow";
            return Regex.IsMatch(colorKey, @"^[0-9a-f]{3}([0-9a-f]{3})?$") ? $"#{colorKey}" : colorKey;
        }

        private string CleanupMarkdown(string markdown)
        {
            // Translate inline-style sentinels into their final Markdown/HTML form first.
            markdown = ResolveFormattingSentinels(markdown);

            // Convert <br> and <br/> tags to proper line breaks FIRST
            // These can appear in tables and regular content
            // Handle all variations: <br>, <br/>, <br />, <br  />, <BR>, etc.
            // Also handle versions with attributes like <br style="...">
            markdown = Regex.Replace(markdown, @"<br[^>]*/?>", "\n", RegexOptions.IgnoreCase);
            
            // Also handle HTML-encoded versions that might slip through
            markdown = Regex.Replace(markdown, @"&lt;br[^&]*/?&gt;", "\n", RegexOptions.IgnoreCase);

            // Restore in-cell line breaks as <br> now that the global <br>->newline step has run, so they
            // stay inside the table row instead of splitting it.
            markdown = markdown.Replace(CellBreak, "<br>");

            // Aggressively find and convert ALL <a>...</a> tags to Markdown links
            // This regex handles any whitespace/newlines within the tag
            markdown = ConvertAllAnchorTags(markdown);

            // Fix escaped underscores in existing Markdown links [text](url)
            // URLs should not have escaped underscores
            markdown = Regex.Replace(markdown,
                @"\]\(([^)]+)\)",
                match => {
                    var url = match.Groups[1].Value;
                    url = url.Replace("\\_", "_");
                    return $"]({url})";
                });

            // Fix escaped underscores in general text
            // ReverseMarkdown escapes underscores to prevent italic formatting,
            // but this looks wrong in code, variable names, etc.
            // We'll unescape all \_ to _ since OneNote doesn't use markdown formatting
            markdown = markdown.Replace("\\_", "_");

            // Fix escaped asterisks in general text
            // Same reasoning - OneNote content shouldn't have escaped asterisks
            markdown = markdown.Replace("\\*", "*");

            // Convert naked URL links [url](url) to <url> format
            // This handles cases where the link text matches the URL
            markdown = Regex.Replace(markdown, 
                @"\[([^\]]+)\]\((\1)\)", 
                match => {
                    var url = match.Groups[1].Value;
                    return $"<{url}>";
                });
            
            // Also handle URL-encoded variations where link text is URL-decoded version
            markdown = Regex.Replace(markdown,
                @"\[(https?://[^\]]+)\]\((https?://[^\)]+)\)",
                match => {
                    var linkText = match.Groups[1].Value;
                    var href = match.Groups[2].Value;
                    // Normalize both by decoding and comparing
                    var decodedText = Uri.UnescapeDataString(linkText.Replace("\\_", "_"));
                    var decodedHref = Uri.UnescapeDataString(href.Replace("\\_", "_"));
                    if (decodedText == decodedHref || linkText == href)
                    {
                        return $"<{href}>";
                    }
                    return match.Value; // Keep original if they differ
                });

            // Remove excessive blank lines
            markdown = Regex.Replace(markdown, @"\n{3,}", "\n\n");
            
            // Clean up HTML entities that might have slipped through
            markdown = markdown.Replace("&nbsp;", " ");
            markdown = markdown.Replace("&amp;", "&");
            markdown = markdown.Replace("&lt;", "<");
            markdown = markdown.Replace("&gt;", ">");
            markdown = markdown.Replace("&quot;", "\"");

            markdown = WrapBareUrls(markdown);
            
            // Remove empty paragraphs
            markdown = Regex.Replace(markdown, @"\n\n\n+", "\n\n");
            
            // Trim lines
            var lines = markdown.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd();
            }
            
            return string.Join("\n", lines).Trim();
        }

        private static string WrapBareUrls(string markdown)
        {
            return Regex.Replace(markdown,
                @"https?://[^\s<>\]]+",
                match => {
                    if (IsAlreadyAutolink(markdown, match.Index) || IsMarkdownLinkDestination(markdown, match.Index))
                    {
                        return match.Value;
                    }

                    var url = match.Value;
                    var trailing = "";

                    while (url.Length > 0 && IsTrailingUrlPunctuation(url[^1]))
                    {
                        trailing = url[^1] + trailing;
                        url = url[..^1];
                    }

                    return url.Length == 0 ? match.Value : $"<{url}>{trailing}";
                });
        }

        private static bool IsAlreadyAutolink(string markdown, int urlStartIndex)
        {
            return urlStartIndex > 0 && markdown[urlStartIndex - 1] == '<';
        }

        private static bool IsMarkdownLinkDestination(string markdown, int urlStartIndex)
        {
            return urlStartIndex > 1 && markdown[urlStartIndex - 1] == '(' && markdown[urlStartIndex - 2] == ']';
        }

        private static bool IsTrailingUrlPunctuation(char character)
        {
            return character == '.'
                || character == ','
                || character == ';'
                || character == ':'
                || character == '!'
                || character == '?'
                || character == ')';
        }

        /// <summary>
        /// Finds and converts all HTML anchor tags to Markdown links.
        /// Handles multiline tags and various attribute formats.
        /// </summary>
        private string ConvertAllAnchorTags(string markdown)
        {
            // Use a loop to find and replace anchor tags one at a time
            // This handles complex cases that regex struggles with
            while (true)
            {
                // Find the start of an anchor tag
                int startIdx = markdown.IndexOf("<a", StringComparison.OrdinalIgnoreCase);
                if (startIdx == -1) break;

                // Find the closing </a>
                int endIdx = markdown.IndexOf("</a>", startIdx, StringComparison.OrdinalIgnoreCase);
                if (endIdx == -1) break;

                int fullEndIdx = endIdx + 4; // Include "</a>"

                // Extract the full anchor tag
                string anchorTag = markdown.Substring(startIdx, fullEndIdx - startIdx);

                // Parse out the href
                string? href = null;
                var hrefMatch = Regex.Match(anchorTag, @"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (hrefMatch.Success)
                {
                    href = hrefMatch.Groups[1].Value.Trim();
                }

                // Parse out the link text (content between > and </a>)
                string? linkText = null;
                int contentStart = anchorTag.IndexOf('>');
                if (contentStart != -1)
                {
                    int contentEnd = anchorTag.LastIndexOf("</a>", StringComparison.OrdinalIgnoreCase);
                    if (contentEnd > contentStart)
                    {
                        linkText = anchorTag.Substring(contentStart + 1, contentEnd - contentStart - 1).Trim();
                    }
                }

                // Convert to Markdown link
                string replacement;
                if (!string.IsNullOrEmpty(href) && !string.IsNullOrEmpty(linkText))
                {
                    // Unescape underscores in URL
                    href = href.Replace("\\_", "_");
                    replacement = ConvertToMarkdownLink(href, linkText);
                }
                else if (!string.IsNullOrEmpty(href))
                {
                    href = href.Replace("\\_", "_");
                    replacement = $"<{href}>";
                }
                else
                {
                    // Can't parse, just remove the tags and keep content
                    replacement = linkText ?? "";
                }

                // Replace the anchor tag with the Markdown link
                markdown = markdown.Substring(0, startIdx) + replacement + markdown.Substring(fullEndIdx);
            }

            return markdown;
        }

        /// <summary>
        /// Converts href and link text to proper Markdown link format.
        /// If text matches the URL (naked URL), uses angle bracket format.
        /// Otherwise uses standard [text](url) format.
        /// </summary>
        private string ConvertToMarkdownLink(string href, string text)
        {
            // Normalize for comparison
            var normalizedText = Uri.UnescapeDataString(text.Replace("\\_", "_").Replace("\\", ""));
            var normalizedHref = Uri.UnescapeDataString(href.Replace("\\_", "_").Replace("\\", ""));

            // Check if this is a naked URL (link text matches URL)
            if (normalizedText == normalizedHref || text == href || 
                text.TrimEnd('/') == href.TrimEnd('/') ||
                normalizedText.TrimEnd('/') == normalizedHref.TrimEnd('/'))
            {
                // Naked URL - use angle bracket format
                return $"<{href}>";
            }

            // Standard link with different text
            return $"[{text}]({href})";
        }
    }
}
