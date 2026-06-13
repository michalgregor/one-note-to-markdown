using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Xsl;

namespace OneNoteMarkdownExporter.Services
{
    /// <summary>
    /// Converts presentation MathML to LaTeX using the embedded XSLT MathML Library (xsltml, MIT licensed -
    /// see Resources/MathML/README). The stylesheets are embedded in the assembly and loaded once.
    /// Returns false (rather than throwing) when the transform can't be built or fails on some input, so
    /// callers can fall back to a simpler converter.
    /// </summary>
    public static class MathmlToLatexConverter
    {
        private const string ResourcePrefix = "OneNoteMarkdownExporter.Resources.MathML.";
        private static readonly Lazy<XslCompiledTransform?> Transform = new(BuildTransform);

        public static bool IsAvailable => Transform.Value != null;

        /// <summary>
        /// Converts a single <c>&lt;math&gt;</c> element (as XML) to its LaTeX body (no $ delimiters).
        /// </summary>
        public static bool TryConvert(string mathXml, out string latex)
        {
            latex = string.Empty;
            var transform = Transform.Value;
            if (transform == null || string.IsNullOrWhiteSpace(mathXml))
            {
                return false;
            }

            try
            {
                using var input = XmlReader.Create(new StringReader(mathXml));
                var builder = new StringBuilder();
                using (var writer = new StringWriter(builder))
                {
                    transform.Transform(input, null, writer);
                }

                latex = builder.ToString().Trim();
                return !string.IsNullOrWhiteSpace(latex);
            }
            catch
            {
                return false;
            }
        }

        private static XslCompiledTransform? BuildTransform()
        {
            try
            {
                var assembly = typeof(MathmlToLatexConverter).Assembly;
                using var entry = assembly.GetManifestResourceStream(ResourcePrefix + "onenote-math.xsl");
                if (entry == null)
                {
                    return null;
                }

                var transform = new XslCompiledTransform();
                using var reader = XmlReader.Create(entry);
                transform.Load(reader, XsltSettings.Default, new EmbeddedResourceResolver(assembly));
                return transform;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Resolves the stylesheet's <c>xsl:include</c> hrefs to the embedded module resources.</summary>
        private sealed class EmbeddedResourceResolver : XmlResolver
        {
            private readonly Assembly _assembly;

            public EmbeddedResourceResolver(Assembly assembly) => _assembly = assembly;

            public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
            {
                var name = Path.GetFileName(relativeUri ?? string.Empty);
                return new Uri("xsltml://mathml/" + name);
            }

            public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
            {
                var name = Path.GetFileName(absoluteUri.AbsolutePath);
                return _assembly.GetManifestResourceStream(ResourcePrefix + name);
            }

            public override ICredentials Credentials { set { /* not needed for embedded resources */ } }
        }
    }
}
