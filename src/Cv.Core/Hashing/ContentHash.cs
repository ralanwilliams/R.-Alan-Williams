using System.Text.Json;
using Cv.Core.Model;

namespace Cv.Core.Hashing;

/// <summary>
/// The <c>content_hash</c> of a version: SHA-256 over a canonical serialisation of the tree
/// and every locale's text. Version metadata (number, summary, author) is not included, so two
/// versions with the same content have the same hash. The database uses it to reject a save
/// that changes nothing.
/// </summary>
/// <remarks>
/// Canonical form (format <see cref="Format"/>): compact JSON with properties in the order
/// written below; nodes sorted by id, texts by node id then locale (ordinal, ids in lowercase
/// "D" format); attrs keys sorted ordinally. Changing any of this changes every hash, so it is
/// versioned: bump <see cref="Format"/> if it ever has to change.
/// </remarks>
public static class ContentHash
{
    public const string Format = "cv-content/1";

    public static Sha256Digest Compute(CvDocument document) => Sha256Digest.Of(Canonicalize(document));

    public static byte[] Canonicalize(CvDocument document)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("format", Format);

            json.WriteStartArray("nodes");
            foreach (var node in document.Nodes.OrderBy(n => Id(n.Id), StringComparer.Ordinal))
            {
                json.WriteStartObject();
                json.WriteString("id", Id(node.Id));
                if (node.ParentId is { } parentId)
                {
                    json.WriteString("parent", Id(parentId));
                }
                else
                {
                    json.WriteNull("parent");
                }
                json.WriteString("type", node.Type);
                json.WriteString("sortKey", node.SortKey);
                json.WriteStartObject("attrs");
                foreach (var (key, value) in node.Attrs.OrderBy(a => a.Key, StringComparer.Ordinal))
                {
                    json.WriteString(key, value);
                }
                json.WriteEndObject();
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("texts");
            foreach (var text in document.Texts
                         .OrderBy(t => Id(t.NodeId), StringComparer.Ordinal)
                         .ThenBy(t => t.Locale, StringComparer.Ordinal))
            {
                json.WriteStartObject();
                json.WriteString("node", Id(text.NodeId));
                json.WriteString("locale", text.Locale);
                if (text.Content is null)
                {
                    json.WriteNull("content");
                }
                else
                {
                    json.WriteString("content", text.Content);
                }
                json.WriteBoolean("omitted", text.IsOmitted);
                if (text.SourceHash is { } sourceHash)
                {
                    json.WriteString("sourceHash", sourceHash.Hex);
                }
                else
                {
                    json.WriteNull("sourceHash");
                }
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static string Id(Guid id) => id.ToString("D");
}
