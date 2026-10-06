using System.Net;
using System.Net.Http.Json;

namespace Cv.Editor.Tests;

/// <summary>The editor has no login, so these rules are its whole security boundary (ADR 0002 §3).</summary>
public sealed class SecurityTests
{
    private static readonly object EmptyAnalyze = new { baseVersionId = (Guid?)null, document = new { nodes = Array.Empty<object>(), texts = Array.Empty<object>() }, locale = "en" };

    [Fact]
    public async Task A_post_from_another_site_is_forbidden()
    {
        await using var app = new EditorFactory();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://evil.example");

        var response = await client.PostAsJsonAsync("/api/analyze", EmptyAnalyze);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_post_without_an_origin_is_forbidden()
    {
        await using var app = new EditorFactory();

        var response = await app.CreateClient().PostAsJsonAsync("/api/analyze", EmptyAnalyze);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_post_from_the_editor_page_is_accepted()
    {
        await using var app = new EditorFactory();

        var response = await app.CreateEditorClient().PostAsJsonAsync("/api/analyze", EmptyAnalyze);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Requests_from_another_computer_are_forbidden()
    {
        await using var app = new EditorFactory();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        request.Headers.Add(EditorFactory.RemoteAddressHeader, "192.168.1.20");

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_foreign_host_header_is_rejected_which_stops_DNS_rebinding()
    {
        await using var app = new EditorFactory();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        request.Headers.Host = "attacker.example";

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_page_is_served_with_a_strict_content_security_policy()
    {
        await using var app = new EditorFactory();

        var response = await app.CreateClient().GetAsync("/");

        response.EnsureSuccessStatusCode();
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("style-src 'self'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("<script type=\"module\" src=\"js/editor.js\">", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task API_responses_are_not_cached()
    {
        await using var app = new EditorFactory();

        var response = await app.CreateClient().GetAsync("/api/session");

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }
}
