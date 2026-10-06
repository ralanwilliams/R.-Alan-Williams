using System.Security.Cryptography;
using System.Text;

namespace Cv.Core.Hashing;

/// <summary>A SHA-256 digest with value equality. Stored as <c>bytea</c> in the database.</summary>
public readonly record struct Sha256Digest
{
    public const int Length = 32;

    private readonly string? _hex;

    private Sha256Digest(string hex) => _hex = hex;

    /// <summary>Lowercase hexadecimal, 64 characters.</summary>
    public string Hex => _hex ?? throw new InvalidOperationException("Uninitialised Sha256Digest.");

    public static Sha256Digest FromBytes(ReadOnlySpan<byte> digest) =>
        digest.Length == Length
            ? new Sha256Digest(Convert.ToHexStringLower(digest))
            : throw new ArgumentException($"A SHA-256 digest is {Length} bytes, got {digest.Length}.", nameof(digest));

    public static Sha256Digest Of(ReadOnlySpan<byte> data) => FromBytes(SHA256.HashData(data));

    /// <summary>
    /// Hash of the text's UTF-8 bytes. Matches PostgreSQL's
    /// <c>sha256(convert_to(text, 'UTF8'))</c>, which is how the database checks staleness.
    /// </summary>
    public static Sha256Digest OfText(string text) => Of(Encoding.UTF8.GetBytes(text));

    public byte[] ToBytes() => Convert.FromHexString(Hex);

    public override string ToString() => Hex;
}
