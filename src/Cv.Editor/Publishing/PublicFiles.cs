using System.Text;
using Cv.Core.Localization;
using Cv.Core.Model;
using Cv.Core.Rendering;
using Cv.Data.Entities;
using Cv.Data.Store;
using Cv.Editor.Pdf;

namespace Cv.Editor.Publishing;

/// <summary>What the backfill stored, or with <c>dryRun</c> would store: one row per version and locale.</summary>
public sealed record BackfillItem(int VersionNumber, string Locale, IReadOnlyList<string> Formats);

/// <summary>
/// Renders the files the public site serves (ADR 0003 §1): for each published locale a PDF, a
/// standalone HTML page and Markdown. They are stored before the publication rows, in the same
/// transaction, and the database refuses a publication without them (§2).
/// </summary>
public sealed class PublicFiles(ICvStore store, IPdfRenderer pdf)
{
    /// <summary>
    /// The stored formats and the renderer version recorded with each. The PDF is printed from the
    /// HTML, so it shares the HTML renderer's version.
    /// </summary>
    public static IReadOnlyList<(string Format, string RendererVersion)> Formats { get; } =
    [
        (CvRender.Pdf, HtmlRenderer.RendererVersion),
        (CvRender.Html, HtmlRenderer.RendererVersion),
        (CvRender.Markdown, MarkdownRenderer.RendererVersion),
    ];

    /// <summary>Every format for each locale, for a version that is not saved yet.</summary>
    public Task<IReadOnlyList<RenderedFile>> RenderAsync(
        CvDocument document, CvCatalog catalog, IReadOnlyList<string> locales, CancellationToken cancellationToken) =>
        RenderAsync(document, catalog, locales.SelectMany(l => Formats.Select(f => (l, f.Format))).ToList(), cancellationToken);

    /// <summary>
    /// The files a saved version still needs before it can be published for these locales: those
    /// with no file from the current renderer. Republishing a version therefore renders nothing,
    /// unless a renderer has changed since its files were stored.
    /// </summary>
    public async Task<IReadOnlyList<RenderedFile>> RenderMissingAsync(Guid versionId, IReadOnlyList<string> locales, CancellationToken cancellationToken)
    {
        var missing = await FindMissingAsync(versionId, locales, cancellationToken);
        if (missing.Count == 0)
        {
            return [];
        }
        return await RenderAsync(
            await store.LoadDocumentAsync(versionId, cancellationToken), await store.GetCatalogAsync(cancellationToken), missing, cancellationToken);
    }

    /// <summary>
    /// Stores current-renderer files for every version and locale that has ever been published:
    /// versions published before files were stored, and all of them after a renderer change. Old
    /// files stay, as <c>cv_renders</c> is append-only; the newest is served. Each version is
    /// stored in its own transaction, so an interrupted run can simply be repeated.
    /// </summary>
    public async Task<IReadOnlyList<BackfillItem>> BackfillAsync(bool dryRun, Action<BackfillItem>? progress, CancellationToken cancellationToken)
    {
        var done = new List<BackfillItem>();
        foreach (var version in (await store.ListEverPublishedAsync(cancellationToken)).GroupBy(p => (p.VersionId, p.VersionNumber)))
        {
            var missing = await FindMissingAsync(version.Key.VersionId, version.Select(p => p.Locale).ToList(), cancellationToken);
            if (missing.Count == 0)
            {
                continue;
            }

            if (!dryRun)
            {
                var files = await RenderAsync(
                    await store.LoadDocumentAsync(version.Key.VersionId, cancellationToken),
                    await store.GetCatalogAsync(cancellationToken), missing, cancellationToken);
                await store.AddFilesAsync(version.Key.VersionId, files, cancellationToken);
            }

            foreach (var locale in missing.GroupBy(m => m.Locale))
            {
                var item = new BackfillItem(version.Key.VersionNumber, locale.Key, locale.Select(m => m.Format).ToList());
                done.Add(item);
                progress?.Invoke(item);
            }
        }
        return done;
    }

    private async Task<IReadOnlyList<(string Locale, string Format)>> FindMissingAsync(
        Guid versionId, IReadOnlyList<string> locales, CancellationToken cancellationToken)
    {
        var stored = (await store.ListFilesAsync(versionId, cancellationToken)).ToHashSet();
        return locales
            .SelectMany(l => Formats.Where(f => !stored.Contains(new StoredFile(l, f.Format, f.RendererVersion))).Select(f => (l, f.Format)))
            .ToList();
    }

    private async Task<IReadOnlyList<RenderedFile>> RenderAsync(
        CvDocument document, CvCatalog catalog, IReadOnlyList<(string Locale, string Format)> wanted, CancellationToken cancellationToken)
    {
        var files = new List<RenderedFile>();
        foreach (var locale in wanted.GroupBy(w => w.Locale))
        {
            var localized = Localizer.Localize(document, catalog, locale.Key);
            var html = HtmlRenderer.Render(localized); // standalone: the stylesheet is inlined
            foreach (var (_, format) in locale)
            {
                var content = format switch
                {
                    CvRender.Pdf => await pdf.RenderAsync(html, cancellationToken),
                    CvRender.Html => Encoding.UTF8.GetBytes(html),
                    CvRender.Markdown => Encoding.UTF8.GetBytes(MarkdownRenderer.Render(localized)),
                    _ => throw new ArgumentOutOfRangeException(nameof(wanted), format, "Unknown file format."),
                };
                files.Add(new RenderedFile(locale.Key, format, Formats.Single(f => f.Format == format).RendererVersion, content));
            }
        }
        return files;
    }
}
