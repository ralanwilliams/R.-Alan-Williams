namespace Cv.Data.Entities;

/// <summary>
/// A rendered file (PDF, Markdown, HTML) for one version, locale and renderer version. Stored when
/// the version is published, and served by the public site (ADR 0003 §1).
/// </summary>
public sealed class CvRender
{
    public const string Pdf = "pdf";
    public const string Markdown = "md";
    public const string Html = "html";

    public required Guid VersionId { get; init; }
    public required string LocaleCode { get; init; }
    public required string Format { get; init; }
    public required string RendererVersion { get; init; }
    /// <summary>SHA-256 of <see cref="Content"/>. The database checks that they match.</summary>
    public required byte[] ContentHash { get; init; }

    public required byte[] Content { get; init; }
    public DateTimeOffset CreatedAt { get; private set; }
}
