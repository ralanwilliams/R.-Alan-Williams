using System.Net;
using System.Net.Http.Json;
using Cv.Editor.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace Cv.Editor.Tests;

/// <summary>
/// The editor through a Cloudflare Tunnel behind Access (ADR 0004). cloudflared runs on the same
/// computer, so these requests come from loopback like local ones; the Access token and the
/// public Origin are what separate them.
/// </summary>
public sealed class RemoteAccessTests
{
    private const string PublicOrigin = $"https://{TestAccess.PublicHost}";

    private static readonly object EmptyAnalyze = new { baseVersionId = (Guid?)null, document = new { nodes = Array.Empty<object>(), texts = Array.Empty<object>() }, locale = "en" };

    /// <summary>A request as cloudflared delivers it: the public Host, Cloudflare's headers and, if given, the Access token.</summary>
    private static HttpRequestMessage ThroughTunnel(HttpMethod method, string path, string? token, string? origin = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Host = TestAccess.PublicHost;
        request.Headers.Add("Cf-Ray", "8a1b2c3d4e5f-OSL");
        request.Headers.Add("Cf-Connecting-IP", "203.0.113.7");
        if (token is not null)
        {
            request.Headers.Add(AccessTokenValidator.HeaderName, token);
        }
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }
        return request;
    }

    [Fact]
    public async Task The_signed_in_author_can_use_the_editor_through_the_tunnel()
    {
        await using var app = new EditorFactory { RemoteAccess = true };
        var client = app.CreateClient();

        var page = await client.SendAsync(ThroughTunnel(HttpMethod.Get, "/", app.Access.CreateToken()));
        var session = await client.SendAsync(ThroughTunnel(HttpMethod.Get, "/api/session", app.Access.CreateToken()));
        var analyze = await client.SendAsync(ThroughTunnel(HttpMethod.Post, "/api/analyze", app.Access.CreateToken(), PublicOrigin, EmptyAnalyze));

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, analyze.StatusCode);
        Assert.Equal("max-age=31536000", Assert.Single(session.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task Without_an_Access_token_nothing_is_served()
    {
        await using var app = new EditorFactory { RemoteAccess = true };

        var response = await app.CreateClient().SendAsync(ThroughTunnel(HttpMethod.Get, "/", token: null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Sign in through Cloudflare Access", await response.Content.ReadAsStringAsync());
    }

    public enum BadToken { Garbage, SignedBySomeoneElse, OtherApplication, OtherTeam, Expired }

    [Theory]
    [InlineData(BadToken.Garbage)]
    [InlineData(BadToken.SignedBySomeoneElse)]
    [InlineData(BadToken.OtherApplication)]
    [InlineData(BadToken.OtherTeam)]
    [InlineData(BadToken.Expired)]
    public async Task An_invalid_token_is_refused(BadToken bad)
    {
        await using var app = new EditorFactory { RemoteAccess = true };
        var token = bad switch
        {
            BadToken.Garbage => "not.a.token",
            BadToken.SignedBySomeoneElse => app.Access.CreateToken(signingKey: TestAccess.NewKey("key-1")), // same kid, wrong key
            BadToken.OtherApplication => app.Access.CreateToken(audience: "another-apps-aud"),
            BadToken.OtherTeam => app.Access.CreateToken(issuer: "https://someone-else.cloudflareaccess.com"),
            BadToken.Expired => app.Access.CreateToken(expires: DateTime.UtcNow.AddMinutes(-5)),
            _ => throw new ArgumentOutOfRangeException(nameof(bad)),
        };

        var response = await app.CreateClient().SendAsync(ThroughTunnel(HttpMethod.Get, "/api/session", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Someone_other_than_the_author_is_refused_even_if_Access_let_them_in()
    {
        await using var app = new EditorFactory { RemoteAccess = true };

        var response = await app.CreateClient().SendAsync(
            ThroughTunnel(HttpMethod.Get, "/api/session", app.Access.CreateToken(email: "mallory@example.com")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("mallory@example.com is not the CV's author", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_author_email_is_compared_without_case()
    {
        await using var app = new EditorFactory { RemoteAccess = true };

        var response = await app.CreateClient().SendAsync(
            ThroughTunnel(HttpMethod.Get, "/api/session", app.Access.CreateToken(email: "Ada@Example.com")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://editor.example.com")]   // not https
    [InlineData("http://localhost")]             // the local origin doesn't count through the tunnel
    [InlineData("https://evil.example")]
    public async Task Changes_through_the_tunnel_need_the_public_https_origin(string? origin)
    {
        await using var app = new EditorFactory { RemoteAccess = true };

        var response = await app.CreateClient().SendAsync(
            ThroughTunnel(HttpMethod.Post, "/api/analyze", app.Access.CreateToken(), origin, EmptyAnalyze));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cloudflare_headers_with_a_local_Host_still_need_a_token()
    {
        // A tunnel route that rewrites Host to localhost must not turn into local access.
        await using var app = new EditorFactory { RemoteAccess = true };
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        request.Headers.Add("Cf-Connecting-IP", "203.0.113.7");

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Local_use_is_unchanged_when_remote_access_is_on()
    {
        await using var app = new EditorFactory { RemoteAccess = true };

        var session = await app.CreateClient().GetAsync("/api/session");
        var analyze = await app.CreateEditorClient().PostAsJsonAsync("/api/analyze", EmptyAnalyze);

        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, analyze.StatusCode);
    }

    [Fact]
    public async Task A_rotated_signing_key_is_fetched_once_and_then_accepted()
    {
        await using var app = new EditorFactory { RemoteAccess = true };
        var client = app.CreateClient();
        app.Access.Rotate();

        var first = await client.SendAsync(ThroughTunnel(HttpMethod.Get, "/api/session", app.Access.CreateToken()));
        var second = await client.SendAsync(ThroughTunnel(HttpMethod.Get, "/api/session", app.Access.CreateToken()));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, app.Access.Refreshes);
    }

    [Fact]
    public async Task With_remote_access_off_the_public_host_and_Cloudflare_traffic_are_refused()
    {
        await using var app = new EditorFactory();
        var client = app.CreateClient();

        var publicHost = await client.SendAsync(ThroughTunnel(HttpMethod.Get, "/api/session", app.Access.CreateToken()));
        var cloudflareHeaders = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        cloudflareHeaders.Headers.Add("Cf-Ray", "8a1b2c3d4e5f-OSL");
        var viaCloudflare = await client.SendAsync(cloudflareHeaders);

        Assert.Equal(HttpStatusCode.BadRequest, publicHost.StatusCode); // host filtering
        Assert.Equal(HttpStatusCode.Forbidden, viaCloudflare.StatusCode);
        Assert.Contains("Remote access to the editor is not set up", await viaCloudflare.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Half_configured_remote_access_stops_the_editor_from_starting()
    {
        await using var app = new EditorFactory().WithWebHostBuilder(builder =>
            builder.UseSetting(EditorOptions.PublicHostVariable, TestAccess.PublicHost)); // no team domain or AUD

        var error = Assert.Throws<OptionsValidationException>(() => app.CreateClient());

        Assert.Contains(EditorOptions.RemoteAccessIncomplete, error.Message);
    }
}
