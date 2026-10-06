namespace Cv.Editor;

/// <summary>Settings, read from environment variables (load <c>.env</c> with <c>scripts/Import-DotEnv.ps1</c>).</summary>
public sealed class EditorOptions
{
    public const string ConnectionStringVariable = "CV_API_CONNECTION";
    public const string UserEmailVariable = "CV_EDITOR_USER_EMAIL";
    public const string ChromiumPathVariable = "CV_CHROMIUM_PATH";
    public const string SeedFileVariable = "CV_SEED_FILE";
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
}
