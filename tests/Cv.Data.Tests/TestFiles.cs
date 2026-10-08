using System.Text;
using Cv.Data.Entities;
using Cv.Data.Store;

namespace Cv.Data.Tests;

/// <summary>Stand-in rendered files: publishing needs one in every format per locale (ADR 0003 §2).</summary>
internal static class TestFiles
{
    public static readonly string[] Formats = [CvRender.Pdf, CvRender.Html, CvRender.Markdown];

    /// <summary>Every format for each locale. The content is "<paramref name="label"/> {locale} {format}".</summary>
    public static IReadOnlyList<RenderedFile> For(string label, params string[] locales) =>
        [.. locales.SelectMany(l => Formats.Select(f => new RenderedFile(l, f, "test/1", Encoding.UTF8.GetBytes($"{label} {l} {f}"))))];
}
