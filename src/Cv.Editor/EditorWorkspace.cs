using Cv.Core.Drafts;
using Cv.Core.Hashing;
using Cv.Core.Model;
using Cv.Data.Store;
using Microsoft.Extensions.Options;

namespace Cv.Editor;

/// <summary>The version a draft was started from. Source hashes and sort keys are derived relative to it.</summary>
public sealed record BaseVersion(VersionInfo Info, CvDocument Document);

/// <summary>A draft built against its base: ready to analyse, render or save.</summary>
public sealed record PreparedDraft(CvCatalog Catalog, BaseVersion? Base, DraftBuildResult Build)
{
    public CvDocument Document => Build.Document;

    /// <summary>False when saving would produce a version identical to the base.</summary>
    public bool HasChanges => Base is null || ContentHash.Compute(Build.Document) != Base.Info.ContentHash;
}

/// <summary>Thrown when the editor is misconfigured in a way only the operator can fix.</summary>
public sealed class EditorConfigurationException(string message) : Exception(message);

/// <summary>Thrown when a request names a base version that does not exist.</summary>
public sealed class UnknownVersionException(Guid versionId) : Exception($"Version {versionId} does not exist.")
{
    public Guid VersionId { get; } = versionId;
}

/// <summary>Shared editor state and the steps every endpoint needs: who the author is, and building a draft against its base.</summary>
public sealed class EditorWorkspace(ICvStore store, IOptions<EditorOptions> options)
{
    private CvUser? _user;

    public async Task<CvUser> GetUserAsync(CancellationToken cancellationToken)
    {
        if (_user is { } user)
        {
            return user;
        }

        var email = options.Value.UserEmail;
        return _user = await store.FindUserAsync(email, cancellationToken)
            ?? throw new EditorConfigurationException(email is null
                ? $"cv.users does not have exactly one row, so the editor can't tell who you are. Set {EditorOptions.UserEmailVariable}."
                : $"No row in cv.users has the email {email}. Create one as described in docs/cv-database.md §6.");
    }

    public async Task<BaseVersion?> LoadBaseAsync(Guid? versionId, CancellationToken cancellationToken)
    {
        if (versionId is not { } id)
        {
            return null;
        }

        var info = await store.GetVersionAsync(id, cancellationToken) ?? throw new UnknownVersionException(id);
        return new BaseVersion(info, await store.LoadDocumentAsync(id, cancellationToken));
    }

    /// <summary>The configured seed file as a draft, or null when none is configured.</summary>
    public async Task<(Draft Draft, string FileName)?> LoadSeedAsync(CancellationToken cancellationToken)
    {
        if (options.Value.SeedFile is not { } configured)
        {
            return null;
        }

        var path = ResolveFromRepositoryRoot(configured);
        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (IOException e)
        {
            throw new EditorConfigurationException($"{EditorOptions.SeedFileVariable} points to {path}, which can't be read: {e.Message}");
        }

        try
        {
            return (DraftOutline.Parse(json, await store.GetCatalogAsync(cancellationToken)), Path.GetFileName(path));
        }
        catch (OutlineFormatException e)
        {
            throw new EditorConfigurationException($"{Path.GetFileName(path)} has a problem at {e.Message}");
        }
    }

    /// <summary>
    /// A relative path is taken from the repository root (the folder with Cv.slnx, where .env
    /// lives), not the current directory: <c>dotnet run --project src/Cv.Editor</c> runs the
    /// app from its project folder. Outside a repository it falls back to the current directory.
    /// </summary>
    internal static string ResolveFromRepositoryRoot(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cv.slnx")))
            {
                return Path.GetFullPath(Path.Combine(directory.FullName, path));
            }
        }
        return Path.GetFullPath(path);
    }

    public async Task<PreparedDraft> PrepareAsync(Guid? baseVersionId, Draft draft, CancellationToken cancellationToken)
    {
        var catalog = await store.GetCatalogAsync(cancellationToken);
        var baseVersion = await LoadBaseAsync(baseVersionId, cancellationToken);
        return new PreparedDraft(catalog, baseVersion, DraftBuilder.Build(draft, baseVersion?.Document, catalog));
    }
}
