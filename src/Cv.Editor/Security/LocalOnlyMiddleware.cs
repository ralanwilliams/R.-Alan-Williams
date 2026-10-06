using System.Net;

namespace Cv.Editor.Security;

/// <summary>
/// The editor's security boundary. It has no login (ADR 0002 §3), so it must only ever act on
/// requests that come from its own page in a browser on this computer.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Loopback only.</b> Kestrel binds to localhost; this also refuses any non-loopback peer
/// (defence in depth, e.g. behind an accidental port forward).</item>
/// <item><b>DNS rebinding</b> is stopped by host filtering (<c>AllowedHosts</c> in appsettings.json):
/// a hostile page that rebinds its own name to 127.0.0.1 still sends its own Host header.</item>
/// <item><b>CSRF.</b> Any page in the browser can send a request to localhost. State-changing
/// requests must carry an <c>Origin</c> equal to the editor's own; browsers always send
/// <c>Origin</c> on cross-origin POSTs and pages cannot forge it.</item>
/// <item><b>Headers:</b> a CSP with no inline script or style, no framing, no referrer.</item>
/// </list>
/// </remarks>
internal sealed class LocalOnlyMiddleware(RequestDelegate next)
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "frame-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; object-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote))
        {
            return Reject(context, "The editor only accepts connections from this computer.");
        }

        if (!IsSafeMethod(context.Request.Method) && !IsSameOrigin(context.Request))
        {
            return Reject(context, "Requests that change data must come from the editor's own page.");
        }

        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers.CacheControl = "no-store";
        }

        return next(context);
    }

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    private static bool IsSameOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        return origin.Length > 0
            && string.Equals(origin, $"{request.Scheme}://{request.Host.Value}", StringComparison.OrdinalIgnoreCase);
    }

    private static Task Reject(HttpContext context, string detail) =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: detail).ExecuteAsync(context);
}
