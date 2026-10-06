using System.Collections.Immutable;
using System.Text;
using Cv.Core.Hashing;
using Cv.Core.Model;
using Cv.Core.Ordering;
using Cv.Core.Validation;

namespace Cv.Core.Drafts;

public sealed record DraftBuildResult(CvDocument Document, IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

/// <summary>
/// Turns the editor's <see cref="Draft"/> into a <see cref="CvDocument"/> ready to be saved as
/// the successor of <c>baseDocument</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Text</b> is trimmed, line endings become <c>\n</c>, and it is normalised to Unicode
/// NFC so that "é" typed two different ways is the same text (and the same hash). Empty text
/// means "no row": the line is then missing in that locale.</item>
/// <item><b>Sort keys</b> come from <see cref="SiblingOrder"/>, keeping the base version's keys
/// wherever the order is unchanged.</item>
/// <item><b>Source hashes</b> (ADR 0001 §6): a translation that is new, edited, or marked as
/// reviewed is taken to match the <i>current</i> source text and gets its hash. An unchanged
/// translation keeps the hash from the base version, so it turns stale when the source changes.</item>
/// </list>
/// Loading a stored version and building it again unchanged yields the same content hash; the
/// tests rely on that, and so does "no changes" detection.
/// </remarks>
public static class DraftBuilder
{
    public static DraftBuildResult Build(Draft draft, CvDocument? baseDocument, CvCatalog catalog)
    {
        var issues = new List<ValidationIssue>();
        var nodes = BuildNodes(draft, baseDocument, issues);
        var texts = BuildTexts(draft, baseDocument, catalog);

        var document = new CvDocument(nodes, texts);
        issues.AddRange(DocumentValidator.Validate(document, catalog));
        return new DraftBuildResult(document, issues);
    }

    public static string NormalizeText(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Trim().Normalize(NormalizationForm.FormC);

    private static List<DocumentNode> BuildNodes(Draft draft, CvDocument? baseDocument, List<ValidationIssue> issues)
    {
        var seen = new HashSet<Guid>();
        var unique = new List<DraftNode>(draft.Nodes.Count);
        foreach (var node in draft.Nodes)
        {
            if (seen.Add(node.Id))
            {
                unique.Add(node);
            }
            else
            {
                issues.Add(new(DocumentValidator.Codes.DuplicateNode, "The same node id appears more than once.", node.Id));
            }
        }

        var nodes = new List<DocumentNode>(unique.Count);
        foreach (var siblings in unique.GroupBy(n => n.ParentId))
        {
            var ordered = siblings.ToList();
            var existingKeys = ordered
                .Select(n => baseDocument?.Find(n.Id) is { } before && before.ParentId == n.ParentId ? before.SortKey : null)
                .ToList();
            var keys = SiblingOrder.Assign(existingKeys);

            for (var i = 0; i < ordered.Count; i++)
            {
                var node = ordered[i];
                nodes.Add(new DocumentNode(node.Id, node.ParentId, node.Type.Trim(), keys[i], NormalizeAttrs(node.Attrs)));
            }
        }
        return nodes;
    }

    private static ImmutableSortedDictionary<string, string> NormalizeAttrs(IReadOnlyDictionary<string, string>? attrs)
    {
        if (attrs is null || attrs.Count == 0)
        {
            return DocumentNode.NoAttrs;
        }

        var builder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in attrs)
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                builder[key.Trim()] = trimmed;
            }
        }
        return builder.ToImmutable();
    }

    private static List<DocumentText> BuildTexts(Draft draft, CvDocument? baseDocument, CvCatalog catalog)
    {
        // Normalise first: source hashes need the final source-locale text.
        var rows = new List<(DraftText Draft, string? Content)>();
        foreach (var text in draft.Texts)
        {
            if (text.Omitted)
            {
                rows.Add((text, null));
            }
            else if (text.Content is { } content && NormalizeText(content) is { Length: > 0 } normalized)
            {
                rows.Add((text, normalized));
            }
            // else: empty text is simply no row.
        }

        var sourceLocale = catalog.Source.Code;
        var sourceText = rows
            .Where(r => r.Draft.Locale == sourceLocale && r.Content is not null)
            .GroupBy(r => r.Draft.NodeId)
            .ToDictionary(g => g.Key, g => g.First().Content!);

        return rows.Select(row =>
        {
            var (text, content) = row;
            Sha256Digest? sourceHash = null;
            if (content is not null && IsTranslation(text.Locale, catalog))
            {
                var before = baseDocument?.TextOf(text.NodeId, text.Locale);
                var unchanged = before is { IsOmitted: false } && before.Content == content;
                sourceHash = unchanged && !text.Reviewed
                    ? before!.SourceHash
                    : sourceText.TryGetValue(text.NodeId, out var source) ? Sha256Digest.OfText(source) : null;
            }
            return new DocumentText(text.NodeId, text.Locale, content, text.Omitted, sourceHash);
        }).ToList();
    }

    private static bool IsTranslation(string locale, CvCatalog catalog) =>
        locale != CvCatalog.NeutralLocale && catalog.FindLocale(locale) is { IsSource: false };
}
