namespace Cv.Data.Entities;

/// <summary>A BCP 47 locale. <c>zxx</c> is the language-neutral pseudo-locale used for names, URLs and the like.</summary>
public sealed class Locale
{
    public const string Neutral = "zxx";

    public required string Code { get; init; }
    public required string Name { get; set; }
    public bool IsSource { get; init; }
    public bool IsPublishable { get; init; }
}
