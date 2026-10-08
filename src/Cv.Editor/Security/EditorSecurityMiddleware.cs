using System.Net;
using Microsoft.Extensions.Options;

namespace Cv.Editor.Security;

/// <summary>
/// The editor's security boundary. It has no login of its own (ADR 0002 §2), so it only acts on
/// requests from its own page, either in a browser on this computer, or through the Cloudflare
/// Tunnel with a valid Cloudflare Access token for the author (ADR 0004).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Loopback only.</b> Kestrel binds to localhost; this also refuses any non-loopback peer
/// (defence in depth, e.g. behind an accidental port forward). <c>cloudflared</c> runs on this
/// computer, so tunnel traffic arrives from loopback too, which is why the next rule exists.</item>
/// <item><b>Through Cloudflare</b> (the public host, or any request carrying Cloudflare's headers):
/// a valid Access token whose email is the author's is required. Without remote access
/// configured, such requests are refused outright.</item>
/// <item><b>DNS rebinding</b> is stopped by host filtering (<c>AllowedHosts</c>, plus the public
/// host when configured): a hostile page that rebinds its own name to 127.0.0.1 still sends its
/// own Host header.</item>
/// <item><b>CSRF.</b> State-changing requests must carry an <c>Origin</c> equal to the editor's
/// own: <c>http://localhost:port</c> locally, <c>https://{public host}</c> through the tunnel.
/// Browsers always send <c>Origin</c> on cross-origin POSTs and pages cannot forge it.</item>
/// <item><b>Headers:</b> a CSP with no inline script or style, no framing, no referrer.</item>
/// </list>
/// </remarks>
internal sealed class EditorSecurityMiddleware(RequestDelegate next, IOptions<EditorOptions> options, AccessTokenValidator access, EditorWorkspace workspace)
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "frame-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; object-src 'none'";

    // Cloudflare's edge adds these; a request on the local machine never has them.
    private static readonly string[] CloudflareHeaders = [AccessTokenValidator.HeaderName, "Cf-Connecting-IP", "Cf-Ray", "Cf-Visitor"];

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote))
        {
            await Reject(context, "The editor only accepts connections from this computer.");
            return;
        }

        string ownOrigin;
        if (IsThroughCloudflare(request, options.Value.PublicHost))
        {
            if (options.Value.PublicHost is not { } publicHost)
            {
                await Reject(context, "Remote access to the editor is not set up.");
                return;
            }

            var identity = await access.ValidateAsync(request.Headers[AccessTokenValidator.HeaderName], context.RequestAborted);
            if (identity is null)
            {
                await Reject(context, "Sign in through Cloudflare Access first.");
                return;
            }
            var author = await workspace.GetUserAsync(context.RequestAborted);
            if (!string.Equals(identity.Email, author.Email, StringComparison.OrdinalIgnoreCase))
            {
                await Reject(context, $"{identity.Email} is not the CV's author.");
                return;
            }

            ownOrigin = $"https://{publicHost}";
            context.Response.Headers.StrictTransportSecurity = "max-age=31536000";
        }
        else
        {
            ownOrigin = $"{request.Scheme}://{request.Host.Value}";
        }

        if (!IsSafeMethod(request.Method) && !string.Equals(request.Headers.Origin.ToString(), ownOrigin, StringComparison.OrdinalIgnoreCase))
        {
            await Reject(context, "Requests that change data must come from the editor's own page.");
            return;
        }

        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        if (request.Path.StartsWithSegments("/api"))
        {
            headers.CacheControl = "no-store";
        }

        await next(context);
    }

    private static bool IsThroughCloudflare(HttpRequest request, string? publicHost) =>
        (publicHost is not null && string.Equals(request.Host.Host, publicHost, StringComparison.OrdinalIgnoreCase))
        || CloudflareHeaders.Any(request.Headers.ContainsKey);

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    private static Task Reject(HttpContext context, string detail) =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: detail).ExecuteAsync(context);
}
