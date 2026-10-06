using Cv.Editor.Pdf;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace Cv.Editor;

/// <summary>
/// Turns the failures the author can act on into readable problem responses. Anything else
/// falls through to the default handler: a generic 500, with the details only in the log.
/// </summary>
internal sealed class EditorExceptionHandler(IProblemDetailsService problemDetails, ILogger<EditorExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        (int Status, string Title, string Detail)? problem = exception switch
        {
            EditorConfigurationException e => (StatusCodes.Status500InternalServerError, "The editor is not set up yet", e.Message),
            PdfUnavailableException e => (StatusCodes.Status503ServiceUnavailable, "Cannot create PDFs", e.Message),
            NpgsqlException or TimeoutException => (StatusCodes.Status503ServiceUnavailable, "Cannot reach the database",
                "Check your connection and CV_API_CONNECTION (docs/cv-editor.md, Troubleshooting)."),
            _ => null,
        };
        if (problem is not { } p)
        {
            return false;
        }

        logger.LogWarning(exception, "{Title}", p.Title);
        context.Response.StatusCode = p.Status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = { Status = p.Status, Title = p.Title, Detail = p.Detail },
        });
    }
}
