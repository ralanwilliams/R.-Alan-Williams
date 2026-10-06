using Cv.Core.Hashing;
using Cv.Core.Model;

namespace Cv.Core.Localization;

/// <summary>One node as it appears in one locale.</summary>
/// <param name="Content">The text to show: the locale's own text, else the language-neutral text; null when hidden or the type has no text.</param>
/// <param name="IsHidden">This node or an ancestor is omitted in this locale.</param>
/// <param name="IsMissing">Needs text in this locale and has none. Blocks publishing.</param>
/// <param name="IsStale">A translation of source text that has changed since. Publishing is allowed, with a warning.</param>
public sealed record LocalizedNode(
    DocumentNode Node,
    int Depth,
    string? Content,
    bool IsHidden,
    bool IsMissing,
    bool IsStale);

public sealed record LocaleReadiness(string Locale, int MissingCount, int StaleCount)
{
    public bool CanPublish => MissingCount == 0;
}

/// <summary>A document resolved for one locale, in document order (hidden nodes included and flagged).</summary>
public sealed record LocalizedDocument(LocaleInfo Locale, IReadOnlyList<LocalizedNode> Nodes)
{
    public LocaleReadiness Readiness { get; } = new(
        Locale.Code,
        Nodes.Count(n => n.IsMissing),
        Nodes.Count(n => n.IsStale));
}

/// <summary>
/// The C# twin of <c>cv.cv_version_localized</c> (ADR 0001 §6): resolves every node for a
/// locale with the <c>zxx</c> fallback, inherited omission, and the missing and stale flags.
/// </summary>
/// <remarks>
/// The editor needs these answers for an unsaved draft on every keystroke, so they can't come
/// from the database. The database function stays authoritative for publishing, and an
/// integration test checks that the two agree on the same document.
/// </remarks>
public static class Localizer
{
    public static IReadOnlyList<LocalizedDocument> LocalizeAll(CvDocument document, CvCatalog catalog) =>
        catalog.PublishableLocales.Select(l => Localize(document, catalog, l.Code)).ToArray();

    public static LocalizedDocument Localize(CvDocument document, CvCatalog catalog, string localeCode)
    {
        var locale = catalog.FindLocale(localeCode)
            ?? throw new ArgumentException($"Unknown locale '{localeCode}'.", nameof(localeCode));

        var hidden = new Dictionary<Guid, bool>();
        var nodes = new List<LocalizedNode>();
        foreach (var (node, depth) in document.InDocumentOrder())
        {
            var own = document.TextOf(node.Id, locale.Code);
            var neutral = document.TextOf(node.Id, CvCatalog.NeutralLocale);
            var source = document.TextOf(node.Id, catalog.Source.Code);
            var hasText = catalog.FindType(node.Type)?.HasText ?? true;

            var isHidden = (node.ParentId is { } parentId && hidden.GetValueOrDefault(parentId)) || own?.IsOmitted == true;
            hidden[node.Id] = isHidden;

            var content = isHidden || !hasText ? null : own?.Content ?? neutral?.Content;
            var isMissing = hasText && !isHidden && own?.Content is null && neutral?.Content is null;
            var isStale = hasText && !isHidden && !locale.IsSource
                && own?.Content is not null
                && source?.Content is { } sourceContent
                && own.SourceHash != Sha256Digest.OfText(sourceContent);

            nodes.Add(new LocalizedNode(node, depth, content, isHidden, isMissing, isStale));
        }

        return new LocalizedDocument(locale, nodes);
    }
}
