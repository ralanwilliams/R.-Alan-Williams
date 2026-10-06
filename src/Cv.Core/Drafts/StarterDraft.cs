using Cv.Core.Model;

namespace Cv.Core.Drafts;

/// <summary>
/// The document the editor opens when no version exists yet: a skeleton CV with the author's
/// name and email filled in (language-neutral) and section headings in every locale the
/// starter knows. Everything else is empty, so it shows up as missing until written.
/// </summary>
public static class StarterDraft
{
    private static readonly Dictionary<string, Dictionary<string, string>> SectionTitles = new(StringComparer.Ordinal)
    {
        ["experience"] = new(StringComparer.Ordinal) { ["en"] = "Experience", ["nb"] = "Erfaring", ["fr"] = "Expérience" },
        ["education"] = new(StringComparer.Ordinal) { ["en"] = "Education", ["nb"] = "Utdanning", ["fr"] = "Formation" },
        ["skills"] = new(StringComparer.Ordinal) { ["en"] = "Skills", ["nb"] = "Ferdigheter", ["fr"] = "Compétences" },
    };

    public static Draft Create(string displayName, string email, CvCatalog catalog)
    {
        var nodes = new List<DraftNode>();
        var texts = new List<DraftText>();

        Guid Add(Guid? parent, string type, IReadOnlyDictionary<string, string>? attrs = null)
        {
            var id = Guid.CreateVersion7();
            nodes.Add(new DraftNode(id, parent, type, attrs));
            return id;
        }

        void Translate(Guid node, string section)
        {
            foreach (var (locale, title) in SectionTitles[section])
            {
                if (catalog.FindLocale(locale) is { IsPublishable: true })
                {
                    texts.Add(new DraftText(node, locale, title));
                }
            }
        }

        var root = Add(null, CvCatalog.RootType);

        var name = Add(root, "name");
        texts.Add(new DraftText(name, CvCatalog.NeutralLocale, displayName));
        Add(root, "headline");
        var contact = Add(root, "contact", new Dictionary<string, string> { [NodeAttributes.Kind] = NodeAttributes.ContactKinds.Email });
        texts.Add(new DraftText(contact, CvCatalog.NeutralLocale, email));

        var experience = Add(root, "section");
        Translate(experience, "experience");
        var job = Add(experience, "entry");
        Add(job, "subtitle");
        Add(job, "location");
        Add(job, "bullet");

        var education = Add(root, "section");
        Translate(education, "education");
        var degree = Add(education, "entry");
        Add(degree, "subtitle");

        var skills = Add(root, "section");
        Translate(skills, "skills");
        var group = Add(skills, "skill_group");
        Add(group, "skill");

        return new Draft(nodes, texts);
    }
}
