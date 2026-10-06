using System.Text;
using Cv.Core.Localization;
using Cv.Core.Model;

namespace Cv.Core.Rendering;

/// <param name="Preview">
/// Editor preview: missing text shows as a highlighted placeholder, stale text is marked, and
/// elements carry <c>data-node-id</c> so a click can jump to the line. Off for downloads.
/// </param>
/// <param name="StylesheetHref">
/// Link the stylesheet from this URL instead of inlining it. The editor does this so its
/// Content Security Policy can forbid inline styles.
/// </param>
public sealed record RenderOptions(bool Preview = false, string? StylesheetHref = null);

/// <summary>
/// Renders one locale of a CV as a standalone HTML document: the editor's live preview, and
/// the input to PDF generation. Semantic markup, every string HTML-encoded, links limited to
/// safe schemes, and <c>lang</c> set so the browser hyphenates correctly.
/// </summary>
public static class HtmlRenderer
{
    /// <summary>Recorded with stored renders (<c>cv_renders.renderer_version</c>). Bump whenever the output changes.</summary>
    public const string RendererVersion = "html/3";

    private static readonly Lazy<string> Stylesheet = new(() =>
    {
        using var stream = typeof(HtmlRenderer).Assembly.GetManifestResourceStream("Cv.Core.Rendering.cv.css")
            ?? throw new InvalidOperationException("Embedded stylesheet Cv.Core.Rendering.cv.css is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <summary>The CSS that rendered documents use, for serving at <see cref="RenderOptions.StylesheetHref"/>.</summary>
    public static string Css => Stylesheet.Value;

    public static string Render(LocalizedDocument document, RenderOptions? options = null)
    {
        options ??= new RenderOptions();
        var context = new RenderContext(document, options, Messages.For(document.Locale.Code));
        var html = new StringBuilder(8 * 1024);

        var name = document.Nodes.FirstOrDefault(n => n.Node.Type == "name" && !n.IsHidden)?.Content;
        var title = name is null ? "CV" : context.Messages.DocumentTitle.Replace("{name}", name);

        html.Append("<!doctype html>\n<html lang=\"").Append(Encode(document.Locale.Code)).Append("\">\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append("<title>").Append(Encode(title)).Append("</title>\n");
        if (options.StylesheetHref is { } href)
        {
            html.Append("<link rel=\"stylesheet\" href=\"").Append(Encode(href)).Append("\">\n");
        }
        else
        {
            html.Append("<style>\n").Append(Css).Append("</style>\n");
        }
        html.Append("</head>\n<body>\n<main class=\"cv").Append(options.Preview ? " cv-preview" : "").Append("\">\n");

        if (context.Tree.Root is { } root)
        {
            RenderTopLevel(html, context, context.ChildrenOf(root.Node.Id));
        }

        html.Append("</main>\n</body>\n</html>\n");
        return html.ToString();
    }

    private sealed class RenderContext(LocalizedDocument document, RenderOptions options, Messages messages)
    {
        public VisibleTree Tree { get; } = new(document);
        public string Locale { get; } = document.Locale.Code;
        public RenderOptions Options { get; } = options;
        public Messages Messages { get; } = messages;

        public IReadOnlyList<LocalizedNode> ChildrenOf(Guid id) => Tree.ChildrenOf(id);
    }

    /// <summary>Name, headline and contacts form the header; sections follow.</summary>
    private static void RenderTopLevel(StringBuilder html, RenderContext context, IReadOnlyList<LocalizedNode> nodes)
    {
        var header = nodes.TakeWhile(VisibleTree.IsHeader).ToList();
        if (header.Count > 0)
        {
            html.Append("<header class=\"cv-header\">\n");
            RenderNodes(html, context, header);
            html.Append("</header>\n");
        }
        RenderNodes(html, context, nodes.Skip(header.Count).ToList());
    }

    /// <summary>
    /// Renders siblings. Runs of bullets and contacts become one list; a run of skills becomes
    /// one comma-separated line.
    /// </summary>
    private static void RenderNodes(StringBuilder html, RenderContext context, IReadOnlyList<LocalizedNode> nodes)
    {
        for (var i = 0; i < nodes.Count;)
        {
            var type = nodes[i].Node.Type;
            if (type == "skill")
            {
                var skills = new List<LocalizedNode>();
                for (; i < nodes.Count && nodes[i].Node.Type == type; i++)
                {
                    skills.Add(nodes[i]);
                }
                if (skills.Any(s => HasVisibleContent(context, s)))
                {
                    html.Append("<p class=\"cv-skills\">");
                    AppendSkills(html, context, skills);
                    html.Append("</p>\n");
                }
                continue;
            }

            if (ListClass(type) is { } listClass)
            {
                var items = new List<LocalizedNode>();
                for (; i < nodes.Count && nodes[i].Node.Type == type; i++)
                {
                    if (HasVisibleContent(context, nodes[i]))
                    {
                        items.Add(nodes[i]);
                    }
                }
                if (items.Count > 0)
                {
                    html.Append("<ul class=\"").Append(listClass).Append("\">\n");
                    foreach (var item in items)
                    {
                        html.Append("<li").Append(Attributes(context, item)).Append('>');
                        AppendContent(html, context, item);
                        html.Append("</li>\n");
                    }
                    html.Append("</ul>\n");
                }
                continue;
            }

            RenderNode(html, context, nodes[i]);
            i++;
        }
    }

    private static string? ListClass(string type) => type switch
    {
        "bullet" => "cv-bullets",
        "contact" => "cv-contacts",
        _ => null,
    };

    private static void RenderNode(StringBuilder html, RenderContext context, LocalizedNode node)
    {
        var attributes = Attributes(context, node);
        var children = context.ChildrenOf(node.Node.Id);
        switch (node.Node.Type)
        {
            case "name":
                Element(html, context, node, "h1", "cv-name", attributes);
                break;
            case "headline":
                Element(html, context, node, "p", "cv-headline", attributes);
                break;
            case "section":
                html.Append("<section class=\"cv-section\"").Append(attributes).Append(">\n");
                Element(html, context, node, "h2", "cv-section-title", "");
                RenderNodes(html, context, children);
                html.Append("</section>\n");
                break;
            case "entry":
                html.Append("<article class=\"cv-entry\"").Append(attributes).Append(">\n");
                RenderEntryHeader(html, context, node, children);
                RenderNodes(html, context, children.Where(c => !VisibleTree.IsEntryHeader(c)).ToList());
                html.Append("</article>\n");
                break;
            case "subtitle" or "location":
                Element(html, context, node, "p", "cv-" + node.Node.Type, attributes); // only reached outside an entry
                break;
            case "skill_group":
                html.Append("<p class=\"cv-skill-group\"").Append(attributes).Append('>');
                if (HasVisibleContent(context, node))
                {
                    html.Append("<span class=\"cv-skill-group-label\">");
                    if (node.Content is { } label)
                    {
                        var text = Typography.Apply(context.Locale, context.Messages.Label.Replace("{label}", label));
                        html.Append(Encode(text));
                    }
                    else
                    {
                        AppendContent(html, context, node); // the missing-text placeholder
                    }
                    html.Append("</span> ");
                }
                AppendSkills(html, context, children);
                html.Append("</p>\n");
                break;
            default: // paragraph, and any type added later
                Element(html, context, node, "p", "cv-" + node.Node.Type.Replace('_', '-'), attributes);
                RenderNodes(html, context, children);
                break;
        }
    }

    /// <summary>
    /// The entry's two header lines: "Organisation | Location", then "Title | dates".
    /// Either part of a line may be absent; a line with nothing to show is left out.
    /// </summary>
    private static void RenderEntryHeader(StringBuilder html, RenderContext context, LocalizedNode entry, IReadOnlyList<LocalizedNode> children)
    {
        var where = children.Where(c => VisibleTree.IsEntryHeader(c) && HasVisibleContent(context, c))
            .OrderBy(c => c.Node.Type == "location") // organisation first
            .ToList();
        if (where.Count > 0)
        {
            html.Append("<p class=\"cv-entry-where\">");
            for (var i = 0; i < where.Count; i++)
            {
                if (i > 0)
                {
                    html.Append(Separator);
                }
                html.Append("<span class=\"cv-").Append(where[i].Node.Type).Append('"').Append(Attributes(context, where[i])).Append('>');
                AppendContent(html, context, where[i]);
                html.Append("</span>");
            }
            html.Append("</p>\n");
        }

        var dates = context.Messages.FormatRange(
            entry.Node.Attrs.GetValueOrDefault(NodeAttributes.Start),
            entry.Node.Attrs.GetValueOrDefault(NodeAttributes.End));
        var hasTitle = HasVisibleContent(context, entry);
        if (hasTitle || dates is not null)
        {
            html.Append("<h3 class=\"cv-entry-what\">");
            if (hasTitle)
            {
                html.Append("<span class=\"cv-entry-title\">");
                AppendContent(html, context, entry);
                html.Append("</span>");
            }
            if (dates is not null)
            {
                html.Append(hasTitle ? Separator : "").Append("<span class=\"cv-dates\">").Append(Encode(dates)).Append("</span>");
            }
            html.Append("</h3>\n");
        }
    }

    /// <summary>Skills as one line, comma separated, each in its own element so the preview can point at it.</summary>
    private static void AppendSkills(StringBuilder html, RenderContext context, IEnumerable<LocalizedNode> skills)
    {
        var first = true;
        foreach (var skill in skills.Where(s => s.Node.Type == "skill" && HasVisibleContent(context, s)))
        {
            if (!first)
            {
                html.Append(", ");
            }
            first = false;
            html.Append("<span class=\"cv-skill\"").Append(Attributes(context, skill)).Append('>');
            AppendContent(html, context, skill);
            html.Append("</span>");
        }
    }

    private const string Separator = "<span class=\"cv-sep\"> | </span>";

    private static void Element(StringBuilder html, RenderContext context, LocalizedNode node, string tag, string cssClass, string attributes)
    {
        if (!HasVisibleContent(context, node))
        {
            return; // e.g. a draft downloaded with text still missing: leave the line out, not an empty element
        }
        html.Append('<').Append(tag).Append(" class=\"").Append(cssClass).Append('"').Append(attributes).Append('>');
        AppendContent(html, context, node);
        html.Append("</").Append(tag).Append(">\n");
    }

    /// <summary>Text to show, or (in the preview only) a placeholder for missing text.</summary>
    private static bool HasVisibleContent(RenderContext context, LocalizedNode node) =>
        node.Content is not null || (node.IsMissing && context.Options.Preview);

    private static void AppendContent(StringBuilder html, RenderContext context, LocalizedNode node)
    {
        if (node.Content is null)
        {
            if (node.IsMissing && context.Options.Preview)
            {
                html.Append("<span class=\"cv-missing\">").Append(Encode(context.Messages.Missing)).Append("</span>");
            }
            return;
        }

        if (node.Node.Type == "contact" && ContactLink(node) is { } href)
        {
            html.Append("<a href=\"").Append(Encode(href)).Append("\">").Append(Encode(node.Content)).Append("</a>");
            return;
        }

        var text = Typography.Apply(context.Locale, node.Content);
        html.Append(Encode(text).Replace("\n", "<br>"));
    }

    private static string Attributes(RenderContext context, LocalizedNode node)
    {
        if (!context.Options.Preview)
        {
            return "";
        }
        var attributes = $" data-node-id=\"{node.Node.Id:D}\"";
        if (node.IsStale)
        {
            attributes += " data-stale=\"true\"";
        }
        return attributes;
    }

    /// <summary>Only <c>mailto:</c>, <c>tel:</c> and <c>http(s):</c> links are produced, so stored text can never become a <c>javascript:</c> URL.</summary>
    internal static string? ContactLink(LocalizedNode node)
    {
        var text = node.Content!.Trim();
        switch (node.Node.Attrs.GetValueOrDefault(NodeAttributes.Kind))
        {
            case NodeAttributes.ContactKinds.Email when text.Count(c => c == '@') == 1 && !text.Any(char.IsWhiteSpace):
                return "mailto:" + text;
            case NodeAttributes.ContactKinds.Phone when text.Length > 0 && text.All(c => char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')'):
                return "tel:" + new string(text.Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());
            case NodeAttributes.ContactKinds.Url:
                var candidate = text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text;
                return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                    ? uri.AbsoluteUri
                    : null;
            default:
                return null;
        }
    }

    /// <summary>Encodes the five HTML-significant characters. Safe in text and in double-quoted attributes.</summary>
    internal static string Encode(string value)
    {
        if (value.AsSpan().IndexOfAny("&<>\"'") < 0)
        {
            return value;
        }

        var encoded = new StringBuilder(value.Length + 16);
        foreach (var c in value)
        {
            encoded.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => c.ToString(),
            });
        }
        return encoded.ToString();
    }
}
