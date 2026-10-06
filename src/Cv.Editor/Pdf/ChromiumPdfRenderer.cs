using Microsoft.Extensions.Options;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace Cv.Editor.Pdf;

/// <summary>Turns rendered CV HTML into a PDF.</summary>
public interface IPdfRenderer
{
    Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken);
}

/// <summary>No usable browser: the message says how to fix it.</summary>
public sealed class PdfUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Prints HTML to PDF with a headless Chromium-based browser already on this computer (Chrome,
/// Edge or Chromium), driven through PuppeteerSharp. Using the installed browser avoids a
/// 150 MB download, and Chromium's print engine is what the renderer's CSS is written for:
/// <c>@page</c> size and margins, hyphenation from <c>lang</c>, and system fonts.
/// </summary>
public sealed class ChromiumPdfRenderer(IOptions<EditorOptions> options, ILogger<ChromiumPdfRenderer> logger)
    : IPdfRenderer, IAsyncDisposable
{
    private readonly SemaphoreSlim _launchLock = new(1, 1);
    private IBrowser? _browser;

    public async Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken)
    {
        var browser = await GetBrowserAsync(cancellationToken);
        var page = await browser.NewPageAsync();
        try
        {
            await page.SetContentAsync(html, new SetContentOptions { WaitUntil = [WaitUntilNavigation.Load] });
            await page.EvaluateExpressionAsync("document.fonts.ready.then(() => true)");
            return await page.PdfDataAsync(new PdfOptions
            {
                Format = PaperFormat.A4,
                PreferCSSPageSize = true, // the stylesheet's @page wins
                PrintBackground = true,
                DisplayHeaderFooter = false,
            });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            _browser.Dispose();
        }
        _launchLock.Dispose();
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is { IsConnected: true } running)
        {
            return running;
        }

        await _launchLock.WaitAsync(cancellationToken);
        try
        {
            if (_browser is { IsConnected: true } started)
            {
                return started;
            }

            var path = options.Value.ChromiumPath ?? FindInstalledBrowser()
                ?? throw new PdfUnavailableException(
                    $"No Chrome, Edge or Chromium found. Install one, or set {EditorOptions.ChromiumPathVariable} to its executable.");
            if (!File.Exists(path))
            {
                throw new PdfUnavailableException($"{EditorOptions.ChromiumPathVariable} points to '{path}', which does not exist.");
            }

            logger.LogInformation("Starting headless browser for PDF output: {Path}", path);
            try
            {
                _browser?.Dispose();
                _browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true, ExecutablePath = path });
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new PdfUnavailableException($"Could not start the browser at '{path}': {e.Message}", e);
            }
            return _browser;
        }
        finally
        {
            _launchLock.Release();
        }
    }

    internal static string? FindInstalledBrowser() => CandidatePaths().FirstOrDefault(File.Exists);

    private static IEnumerable<string> CandidatePaths()
    {
        if (OperatingSystem.IsWindows())
        {
            string[] roots =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ];
            foreach (var root in roots.Where(r => r.Length > 0))
            {
                yield return Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
                yield return Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
            yield return "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge";
            yield return "/Applications/Chromium.app/Contents/MacOS/Chromium";
        }
        else
        {
            yield return "/usr/bin/google-chrome";
            yield return "/usr/bin/chromium";
            yield return "/usr/bin/chromium-browser";
            yield return "/usr/bin/microsoft-edge";
        }
    }
}
