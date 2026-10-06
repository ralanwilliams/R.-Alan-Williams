using System.Collections.Immutable;
using System.Text.Json;
using Cv.Core.Hashing;
using Cv.Core.Model;
using Cv.Data.Entities;

namespace Cv.Data.Store;

/// <summary>Converts between the domain model (<see cref="CvDocument"/>) and the EF entities of one version.</summary>
internal static class DocumentMapper
{
    public static CvDocument ToDocument(IEnumerable<CvNode> nodes, IEnumerable<CvNodeContent> contents) => new(
        nodes.Select(n => new DocumentNode(n.NodeId, n.ParentNodeId, n.TypeCode, n.SortKey, ParseAttrs(n.Attrs, n.NodeId))),
        contents.Select(c => new DocumentText(
            c.NodeId,
            c.LocaleCode,
            c.Content,
            c.IsOmitted,
            c.SourceHash is { } hash ? Sha256Digest.FromBytes(hash) : null)));

    public static IEnumerable<CvNode> ToNodes(Guid versionId, CvDocument document) =>
        document.Nodes.Select(n => new CvNode
        {
            VersionId = versionId,
            NodeId = n.Id,
            ParentNodeId = n.ParentId,
            TypeCode = n.Type,
            SortKey = n.SortKey,
            Attrs = JsonSerializer.Serialize(n.Attrs),
        });

    public static IEnumerable<CvNodeContent> ToContents(Guid versionId, CvDocument document) =>
        document.Texts.Select(t => new CvNodeContent
        {
            VersionId = versionId,
            NodeId = t.NodeId,
            LocaleCode = t.Locale,
            Content = t.Content,
            IsOmitted = t.IsOmitted,
            SourceHash = t.SourceHash?.ToBytes(),
        });

    /// <summary>
    /// The database only requires a JSON object; this application stores a flat map of strings
    /// (see <see cref="NodeAttributes"/>). JSON nulls are read as "absent".
    /// </summary>
    private static ImmutableSortedDictionary<string, string> ParseAttrs(string json, Guid nodeId)
    {
        using var parsed = JsonDocument.Parse(json);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"attrs of node {nodeId} is not a JSON object.");
        }

        var attrs = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var property in parsed.RootElement.EnumerateObject())
        {
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    attrs[property.Name] = property.Value.GetString()!;
                    break;
                case JsonValueKind.Null:
                    break;
                default:
                    throw new InvalidDataException(
                        $"attrs.{property.Name} of node {nodeId} is a {property.Value.ValueKind}; this application only stores strings.");
            }
        }
        return attrs.ToImmutable();
    }
}
