using Cv.Editor.Pdf;
using Microsoft.Extensions.Options;

namespace Cv.Editor.Publishing;

/// <summary>
/// <c>dotnet run --project src/Cv.Editor -- backfill [--dry-run]</c>: stores current-renderer files
/// for every version that has ever been published (<see cref="PublicFiles.BackfillAsync"/>). Run it
/// once after the RequireRendersToPublish migration, and again whenever a <c>RendererVersion</c>
/// is bumped (ADR 0003 §2). Uses the editor's settings and its <c>cv_api</c> login.
/// </summary>
internal static class BackfillCommand
{
    public const string Name = "backfill";
    private const string DryRunOption = "--dry-run";

    public static async Task<int> RunAsync(IReadOnlyList<string> options)
    {
        if (options.Any(o => o != DryRunOption))
        {
            Console.Error.WriteLine($"Usage: dotnet run --project src/Cv.Editor -- {Name} [{DryRunOption}]");
            return 2;
        }
        var dryRun = options.Contains(DryRunOption);

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // finish cleanly: each version is its own transaction
            cancel.Cancel();
        };

        // The editor's own services and settings, without starting its web server.
        var builder = WebApplication.CreateBuilder();
        builder.AddCvEditor();
        await using var app = builder.Build();

        Console.WriteLine(dryRun ? "Files that would be rendered (nothing is stored):" : "Rendering and storing files:");
        try
        {
            var items = await app.Services.GetRequiredService<PublicFiles>().BackfillAsync(
                dryRun,
                item => Console.WriteLine($"  v{item.VersionNumber} {item.Locale}: {string.Join(", ", item.Formats)}"),
                cancel.Token);

            Console.WriteLine(items.Count == 0
                ? "  none. Every published version has files from the current renderers."
                : dryRun ? $"{items.Count} version/language pair(s) need files. Run without {DryRunOption} to store them." : "Done.");
            return 0;
        }
        catch (Exception e) when (e is OptionsValidationException or EditorConfigurationException or PdfUnavailableException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Stopped. Versions finished so far are stored; run the command again to continue.");
            return 1;
        }
    }
}
