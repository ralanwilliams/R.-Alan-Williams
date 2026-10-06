using System.Text.Json;
using Cv.Core.Model;

namespace Cv.Core.Drafts;

/// <summary>A problem in an outline file, with the JSON path (<c>$.children[2].en</c>) of what it is about.</summary>
public sealed class OutlineFormatException(string path, string message)
    : FormatException($"{path}: {message}")
{
    public string Path { get; } = path;
}

/// <summary>
/// Reads a CV written by hand as a nested JSON outline, the format of seed files
/// (ADR 0002 §11). Each line is an object; its text is given per locale code:
/// <code>
/// {
///   "children": [
///     { "type": "name", "zxx": "Ada Lovelace" },
///     { "type": "section", "en": "Experience", "children": [
///       { "type": "entry", "en": "Engineer", "attrs": { "start": "2021-03" }, "children": [
///         { "type": "subtitle", "zxx": "Analytical Engines Ltd" },
///         { "type": "location", "en": "London, UK", "fr": "Londres, Royaume-Uni" },
///         { "type": "bullet", "en": "Built the difference engine." }
///       ] }
///     ] }
///   ]
/// }
/// </code>
/// The top level is the root (its <c>type</c> may be omitted). <c>"omit": ["fr"]</c> leaves a
/// line out of those locales. Unknown properties are errors, so a typo is never silently
/// dropped. Comments and trailing commas are allowed. Every line gets a new id, and order is
/// the order in the file. Grammar and attribute rules are checked later, by
/// <see cref="DraftBuilder"/>, against the database's grammar.
/// </summary>
public static class DraftOutline
{
    private const string RootPath = "$";

    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Draft Parse(string json, CvCatalog catalog)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, Options);
        }
        catch (JsonException e)
        {
            throw new OutlineFormatException(RootPath, $"not valid JSON: {e.Message}");
        }

        using (document)
        {
            var nodes = new List<DraftNode>();
            var texts = new List<DraftText>();
            ReadNode(document.RootElement, parentId: null, RootPath, catalog, nodes, texts);
            return new Draft(nodes, texts);
        }
    }

    private static void ReadNode(JsonElement element, Guid? parentId, string path, CvCatalog catalog, List<DraftNode> nodes, List<DraftText> texts)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new OutlineFormatException(path, "a line must be a JSON object.");
        }

        var isRoot = parentId is null;
        var type = element.TryGetProperty("type", out var typeElement) ? String(typeElement, $"{path}.type") : null;
        if (isRoot)
        {
            if (type is not null && type != CvCatalog.RootType)
            {
                throw new OutlineFormatException(path, $"the top level is the document itself, so its type must be \"{CvCatalog.RootType}\" (or left out).");
            }
            type = CvCatalog.RootType;
        }
        else if (type is null)
        {
            throw new OutlineFormatException(path, "every line needs a \"type\".");
        }

        var id = Guid.CreateVersion7();
        Dictionary<string, string>? attrs = null;
        var children = new List<(JsonElement Element, string Path)>();

        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            switch (property.Name)
            {
                case "type":
                    break;
                case "attrs":
                    attrs = ReadAttrs(property.Value, propertyPath);
                    break;
                case "children":
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        throw new OutlineFormatException(propertyPath, "must be an array of lines.");
                    }
                    var index = 0;
                    foreach (var child in property.Value.EnumerateArray())
                    {
                        children.Add((child, $"{propertyPath}[{index++}]"));
                    }
                    break;
                case "omit":
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        throw new OutlineFormatException(propertyPath, "must be an array of locale codes, e.g. [\"fr\"].");
                    }
                    foreach (var locale in property.Value.EnumerateArray())
                    {
                        texts.Add(new DraftText(id, Locale(String(locale, propertyPath), propertyPath, catalog), null, Omitted: true));
                    }
                    break;
                default:
                    if (catalog.FindLocale(property.Name) is null)
                    {
                        throw new OutlineFormatException(propertyPath,
                            $"unknown property. Use \"type\", \"attrs\", \"children\", \"omit\" or a locale code ({string.Join(", ", catalog.Locales.Select(l => l.Code))}).");
                    }
                    texts.Add(new DraftText(id, property.Name, String(property.Value, propertyPath)));
                    break;
            }
        }

        nodes.Add(new DraftNode(id, parentId, type, attrs));
        foreach (var (child, childPath) in children)
        {
            ReadNode(child, id, childPath, catalog, nodes, texts);
        }
    }

    private static Dictionary<string, string> ReadAttrs(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new OutlineFormatException(path, "must be an object, e.g. { \"start\": \"2021-03\" }.");
        }
        return element.EnumerateObject().ToDictionary(p => p.Name, p => String(p.Value, $"{path}.{p.Name}"), StringComparer.Ordinal);
    }

    private static string Locale(string code, string path, CvCatalog catalog) =>
        catalog.FindLocale(code) is not null ? code : throw new OutlineFormatException(path, $"unknown locale \"{code}\".");

    private static string String(JsonElement element, string path) =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString()!
            : throw new OutlineFormatException(path, $"must be a string, not {element.ValueKind.ToString().ToLowerInvariant()}.");
}
