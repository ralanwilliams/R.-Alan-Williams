using System.Text;
using System.Text.RegularExpressions;
using Cv.Core.Localization;
using Cv.Core.Model;

namespace Cv.Core.Rendering;

/// <summary>
/// Renders one locale of a CV as CommonMark, for the public <c>format=md</c> download. Follows
/// the HTML layout (header, then sections with "Organisation | Location" over "Title | dates"),
/// formats dates and French typography the same way, and leaves out lines with no text.
/// </summary>
/// <remarks>
/// Every piece of stored text is escaped, so it can never become a heading, list, link, image,
/// HTML or emphasis. The only links are contacts, through the same safe-scheme check as HTML.
/// </remarks>
public static partial class MarkdownRenderer
{
    /// <summary>Recorded with stored renders (<c>cv_renders.renderer_version</c>). Bump whenever the output changes.</summary>
    public const string RendererVersion = "md/1";

    public static string Render(LocalizedDocument document)
    {
        var context = new RenderContext(document, Messages.For(document.Locale.Code));
        var blocks = new List<string>();

        if (context.Tree.Root is { } root)
        {
            var nodes = context.Tree.ChildrenOf(root.Node.Id);
            var header = nodes.TakeWhile(VisibleTree.IsHeader).ToList();
            RenderNodes(blocks, context, header);
            RenderNodes(blocks, context, nodes.Skip(header.Count).ToList());
        }

        return blocks.Count == 0 ? "" : string.Join("\n\n", blocks) + "\n";
    }

    private sealed class RenderContext(LocalizedDocument document, Messages messages)
    {
        public VisibleTree Tree { get; } = new(document);
        public string Locale { get; } = document.Locale.Code;
        public Messages Messages { get; } = messages;
    }

    /// <summary>
    /// Renders siblings as blocks. Runs of bullets become one list, a run of contacts one
    /// "a | b | c" line, and a run of skills one comma-separated line.
    /// </summary>
    private static void RenderNodes(List<string> blocks, RenderContext context, IReadOnlyList<LocalizedNode> nodes)
    {
        for (var i = 0; i < nodes.Count;)
        {
            var type = nodes[i].Node.Type;
            if (type is "bullet" or "contact" or "skill")
            {
                var run = new List<LocalizedNode>();
                for (; i < nodes.Count && nodes[i].Node.Type == type; i++)
                {
                    if (nodes[i].Content is not null)
                    {
                        run.Add(nodes[i]);
                    }
                }
                if (run.Count > 0)
                {
                    blocks.Add(type switch
                    {
                        "bullet" => string.Join("\n", run.Select(b => "- " + Text(context, b, continuationIndent: "  "))),
                        "contact" => string.Join(" | ", run.Select(c => Contact(context, c))),
                        _ => Skills(context, run),
                    });
                }
                continue;
            }

            RenderNode(blocks, context, nodes[i]);
            i++;
        }
    }

    private static void RenderNode(List<string> blocks, RenderContext context, LocalizedNode node)
    {
        var children = context.Tree.ChildrenOf(node.Node.Id);
        switch (node.Node.Type)
        {
            case "name":
                AddText(blocks, context, node, "# ");
                break;
            case "section":
                AddText(blocks, context, node, "## ");
                RenderNodes(blocks, context, children);
                break;
            case "entry":
                RenderEntryHeader(blocks, context, node, children);
                RenderNodes(blocks, context, children.Where(c => !VisibleTree.IsEntryHeader(c)).ToList());
                break;
            case "skill_group":
                var skills = Skills(context, children.Where(c => c.Node.Type == "skill" && c.Content is not null));
                var label = node.Content is { } text
                    ? "**" + Escape(Typography.Apply(context.Locale, context.Messages.Label.Replace("{label}", text))) + "**"
                    : "";
                var line = string.Join(" ", new[] { label, skills }.Where(s => s.Length > 0));
                if (line.Length > 0)
                {
                    blocks.Add(line);
                }
                break;
            default: // headline, paragraph, a subtitle or location outside an entry, and any type added later
                AddText(blocks, context, node, "");
                RenderNodes(blocks, context, children);
                break;
        }
    }

    /// <summary>"**Organisation** | Location", then a "### Title | dates" heading; as in HTML, an empty line is left out.</summary>
    private static void RenderEntryHeader(List<string> blocks, RenderContext context, LocalizedNode entry, IReadOnlyList<LocalizedNode> children)
    {
        var where = children.Where(c => VisibleTree.IsEntryHeader(c) && c.Content is not null)
            .OrderBy(c => c.Node.Type == "location") // organisation first
            .Select(c => c.Node.Type == "subtitle" ? "**" + Text(context, c) + "**" : Text(context, c))
            .ToList();
        if (where.Count > 0)
        {
            blocks.Add(string.Join(" | ", where));
        }

        var what = new List<string>();
        if (entry.Content is not null)
        {
            what.Add(Text(context, entry, joinLines: " "));
        }
        if (context.Messages.FormatRange(entry.Node.Attrs.GetValueOrDefault(NodeAttributes.Start), entry.Node.Attrs.GetValueOrDefault(NodeAttributes.End)) is { } dates)
        {
            what.Add(Escape(dates));
        }
        if (what.Count > 0)
        {
            blocks.Add("### " + string.Join(" | ", what));
        }
    }

    private static string Skills(RenderContext context, IEnumerable<LocalizedNode> skills) =>
        string.Join(", ", skills.Select(s => Text(context, s)));

    /// <summary>A contact, linked when <see cref="HtmlRenderer.ContactLink"/> finds a safe link.</summary>
    private static string Contact(RenderContext context, LocalizedNode contact)
    {
        var text = Text(context, contact, joinLines: " ");
        if (HtmlRenderer.ContactLink(contact) is not { } href)
        {
            return text;
        }
        // Angle brackets let the destination hold parentheses; it must not hold < or > itself.
        return $"[{Escape(contact.Content!.Trim(), lineStart: false)}](<{href.Replace("<", "%3C").Replace(">", "%3E")}>)";
    }

    private static void AddText(List<string> blocks, RenderContext context, LocalizedNode node, string prefix)
    {
        if (node.Content is not null)
        {
            // A heading must be one line; other blocks keep their line breaks.
            blocks.Add(prefix + Text(context, node, joinLines: prefix.Length > 0 ? " " : null));
        }
    }

    /// <summary>
    /// The node's text with locale typography, escaped. Line breaks become hard breaks
    /// (<c>\</c> at the end of the line), or are joined with <paramref name="joinLines"/> where
    /// a break is not possible. Blank lines are dropped, because they would end the block.
    /// </summary>
    private static string Text(RenderContext context, LocalizedNode node, string? joinLines = null, string continuationIndent = "")
    {
        var lines = Typography.Apply(context.Locale, node.Content!)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l => Escape(l));
        return string.Join(joinLines ?? "\\\n" + continuationIndent, lines);
    }

    /// <summary>
    /// Escapes one line of text so that it is shown literally. Characters that start inline
    /// markup are always escaped. Characters that only matter at the start of a line (headings,
    /// lists, quotes, setext underlines) are escaped there, unless <paramref name="lineStart"/> is
    /// false, as in link text. An <c>&amp;</c> is escaped only where it would start an entity,
    /// so "R&amp;D" stays readable.
    /// </summary>
    internal static string Escape(string line, bool lineStart = true)
    {
        var escaped = new StringBuilder(line.Length + 8);
        foreach (var c in line)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '|' or '~')
            {
                escaped.Append('\\');
            }
            escaped.Append(c);
        }

        var text = EntityStart().Replace(escaped.ToString(), "\\&");
        if (!lineStart)
        {
            return text;
        }
        if (text.Length > 0 && text[0] is '#' or '-' or '+' or '=')
        {
            return "\\" + text;
        }
        return OrderedListStart().Replace(text, "$1\\$2");
    }

    [GeneratedRegex(@"&(?=#?[A-Za-z0-9]+;)")]
    private static partial Regex EntityStart();

    [GeneratedRegex(@"^(\d+)([.)])")]
    private static partial Regex OrderedListStart();
}
