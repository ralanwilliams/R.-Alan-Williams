using Cv.Core.Model;
using Cv.Core.Ordering;

namespace Cv.Core.Validation;

/// <summary>A problem with a document, pinned to a node (and locale) where possible so the editor can point at the line.</summary>
public sealed record ValidationIssue(string Code, string Message, Guid? NodeId = null, string? Locale = null);

/// <summary>
/// Checks a document against every rule the database enforces on <c>cv_nodes</c> and
/// <c>cv_node_contents</c>, plus this application's attribute rules.
/// </summary>
/// <remarks>
/// The database remains the authority (ADR 0001 §10): this exists so the editor can say which
/// line is wrong before a save, instead of relaying the first constraint that fails. The
/// integration tests check that documents accepted here are accepted there.
/// </remarks>
public static class DocumentValidator
{
    public static class Codes
    {
        public const string DuplicateNode = "duplicate-node";
        public const string Root = "root";
        public const string UnknownType = "unknown-type";
        public const string MissingParent = "missing-parent";
        public const string Grammar = "grammar";
        public const string Unreachable = "unreachable";
        public const string SortKey = "sort-key";
        public const string Attributes = "attributes";
        public const string Text = "text";
    }

    public static IReadOnlyList<ValidationIssue> Validate(CvDocument document, CvCatalog catalog)
    {
        var issues = new List<ValidationIssue>();
        ValidateStructure(document, catalog, issues);
        ValidateTexts(document, catalog, issues);
        return issues;
    }

    private static void ValidateStructure(CvDocument document, CvCatalog catalog, List<ValidationIssue> issues)
    {
        foreach (var duplicate in document.Nodes.GroupBy(n => n.Id).Where(g => g.Count() > 1))
        {
            issues.Add(new(Codes.DuplicateNode, "The same node id appears more than once.", duplicate.Key));
        }

        var roots = document.Nodes.Where(n => n.ParentId is null).ToList();
        if (roots.Count != 1)
        {
            issues.Add(new(Codes.Root, $"A document needs exactly one root node; found {roots.Count}."));
        }

        foreach (var node in document.Nodes)
        {
            var isRootType = node.Type == CvCatalog.RootType;
            if (isRootType != (node.ParentId is null))
            {
                issues.Add(new(Codes.Root, isRootType ? "The root cannot have a parent." : "Only the root may have no parent.", node.Id));
            }

            if (catalog.FindType(node.Type) is null)
            {
                issues.Add(new(Codes.UnknownType, $"Unknown node type '{node.Type}'.", node.Id));
            }

            if (node.ParentId is { } parentId)
            {
                if (parentId == node.Id)
                {
                    issues.Add(new(Codes.Unreachable, "A node cannot be its own parent.", node.Id));
                }
                else if (document.Find(parentId) is not { } parent)
                {
                    issues.Add(new(Codes.MissingParent, "The parent node does not exist.", node.Id));
                }
                else if (!catalog.CanContain(parent.Type, node.Type))
                {
                    issues.Add(new(Codes.Grammar, $"A {parent.Type} cannot contain a {node.Type}.", node.Id));
                }
            }

            if (!FractionalIndex.IsValid(node.SortKey))
            {
                issues.Add(new(Codes.SortKey, $"Invalid sort key '{node.SortKey}'.", node.Id));
            }

            foreach (var problem in NodeAttributes.Validate(node.Type, node.Attrs))
            {
                issues.Add(new(Codes.Attributes, problem, node.Id));
            }
        }

        foreach (var clash in document.Nodes
                     .GroupBy(n => (n.ParentId, n.SortKey))
                     .Where(g => g.Count() > 1))
        {
            issues.Add(new(Codes.SortKey, $"Siblings share the sort key '{clash.Key.SortKey}'.", clash.First().Id));
        }

        // Anything not reachable from the single root is either in a cycle or hangs off a
        // missing parent (already reported above).
        if (roots.Count == 1)
        {
            var reachable = document.InDocumentOrder().Select(x => x.Node.Id).ToHashSet();
            foreach (var node in document.Nodes.Where(n => !reachable.Contains(n.Id)))
            {
                if (node.ParentId is { } parentId && parentId != node.Id && document.Find(parentId) is not null)
                {
                    issues.Add(new(Codes.Unreachable, "This node is not connected to the root (its ancestors form a cycle).", node.Id));
                }
            }
        }
    }

    private static void ValidateTexts(CvDocument document, CvCatalog catalog, List<ValidationIssue> issues)
    {
        foreach (var duplicate in document.Texts.GroupBy(t => (t.NodeId, t.Locale)).Where(g => g.Count() > 1))
        {
            issues.Add(new(Codes.Text, "More than one text for this node and locale.", duplicate.Key.NodeId, duplicate.Key.Locale));
        }

        foreach (var text in document.Texts)
        {
            void Report(string message) => issues.Add(new(Codes.Text, message, text.NodeId, text.Locale));

            var node = document.Find(text.NodeId);
            if (node is null)
            {
                Report("Text belongs to a node that does not exist.");
                continue;
            }

            var locale = catalog.FindLocale(text.Locale);
            if (locale is null)
            {
                Report($"Unknown locale '{text.Locale}'.");
                continue;
            }

            if (text.IsOmitted != (text.Content is null))
            {
                Report(text.IsOmitted ? "An omitted line cannot have text." : "Text is missing; mark the line as omitted instead.");
            }
            if (text.Content is not null && string.IsNullOrWhiteSpace(text.Content))
            {
                Report("Text cannot be blank.");
            }

            var isNeutral = text.Locale == CvCatalog.NeutralLocale;
            if (isNeutral && text.IsOmitted)
            {
                Report("Language-neutral text cannot be omitted.");
            }
            if (text.SourceHash is not null && (isNeutral || text.IsOmitted || locale.IsSource))
            {
                Report("Only a translation into a target language records which source text it came from.");
            }

            if (catalog.FindType(node.Type) is { HasText: false } && !text.IsOmitted)
            {
                Report($"A {node.Type} carries no text; it can only be omitted.");
            }
        }
    }
}
