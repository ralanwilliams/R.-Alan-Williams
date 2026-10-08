namespace Cv.Editor;

/// <summary>Settings, read from environment variables (load <c>.env</c> with <c>scripts/Import-DotEnv.ps1</c>).</summary>
public sealed class EditorOptions
{
    public const string ConnectionStringVariable = "CV_API_CONNECTION";
    public const string UserEmailVariable = "CV_EDITOR_USER_EMAIL";
    public const string ChromiumPathVariable = "CV_CHROMIUM_PATH";
    public const string SeedFileVariable = "CV_SEED_FILE";
    public const string PublicHostVariable = "CV_EDITOR_PUBLIC_HOST";
    public const string AccessTeamDomainVariable = "CV_ACCESS_TEAM_DOMAIN";
    public const string AccessAudienceVariable = "CV_ACCESS_AUD";
    public const string PortSetting = "Editor:Port";
    public const int DefaultPort = 5180;

    /// <summary>
    /// Connection string for a login role that is a member of <c>cv_app</c> (e.g. <c>cv_api</c>),
    /// never <c>postgres</c>: the editor only needs SELECT and INSERT (ADR 0001 §11).
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Which <c>cv.users</c> row is the author. Optional while there is exactly one user.</summary>
    public string? UserEmail { get; set; }

    /// <summary>Chrome, Edge or Chromium executable for PDF output. Optional: common install locations are searched.</summary>
    public string? ChromiumPath { get; set; }

    /// <summary>
    /// An outline file (see <c>DraftOutline</c>) to open while no version exists, instead of the
    /// starter document. Relative paths are resolved from the repository root. Nothing is
    /// written until the author saves (ADR 0002 §11).
    /// </summary>
    public string? SeedFile { get; set; }

    /// <summary>
    /// The hostname a Cloudflare Tunnel serves the editor at, e.g. <c>editor.ralanwilliams.com</c>
    /// (ADR 0004). Null keeps the editor local-only. When set, <see cref="AccessTeamDomain"/> and
    /// <see cref="AccessAudience"/> are required: every request through the tunnel must carry a
    /// valid Cloudflare Access token for the author.
    /// </summary>
    public string? PublicHost { get; set; }

    /// <summary>The Zero Trust team domain that signs Access tokens, e.g. <c>ralanwilliams.cloudflareaccess.com</c>.</summary>
    public string? AccessTeamDomain { get; set; }

    /// <summary>The Access application's AUD tag: tokens for any other application are refused.</summary>
    public string? AccessAudience { get; set; }

    public const string RemoteAccessIncomplete =
        $"Remote access needs all three of {PublicHostVariable}, {AccessTeamDomainVariable} and " +
        $"{AccessAudienceVariable}, or none of them (docs/cv-editor.md, Remote access).";

    /// <summary>Remote access is either off (none of the three settings) or fully configured.</summary>
    public bool RemoteAccessIsConsistent =>
        (PublicHost is null) == (AccessTeamDomain is null) && (PublicHost is null) == (AccessAudience is null);
}
