namespace Cv.Data.Entities;

/// <summary>A cached rendered file (PDF, Markdown, HTML) for one version, locale and renderer version.</summary>
public sealed class CvRender
{
    public const string Pdf = "pdf";
    public const string Markdown = "md";
    public const string Html = "html";

    public required Guid VersionId { get; init; }
    public required string LocaleCode { get; init; }
    public required string Format { get; init; }
    public required string RendererVersion { get; init; }
    public required byte[] ContentHash { get; init; }
    public required string StorageKey { get; init; }
    public DateTimeOffset CreatedAt { get; private set; }
}
