using System.Text.Json.Serialization;
using Cv.Data;
using Cv.Data.Store;
using Cv.Editor.Api;
using Cv.Editor.Pdf;
using Cv.Editor.Publishing;
using Cv.Editor.Security;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Options;

namespace Cv.Editor;

/// <summary>Service registration and the request pipeline, kept out of Program.cs so tests host exactly the same app.</summary>
public static class EditorHost
{
    public static WebApplicationBuilder AddCvEditor(this WebApplicationBuilder builder)
    {
        // Loopback only, whatever ASPNETCORE_URLS says. The editor has no login of its own
        // (ADR 0002): being unreachable from other machines is part of its security model.
        builder.WebHost.ConfigureKestrel((context, kestrel) =>
            kestrel.ListenLocalhost(context.Configuration.GetValue(EditorOptions.PortSetting, EditorOptions.DefaultPort)));

        builder.Services.AddOptions<EditorOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.ConnectionString = configuration[EditorOptions.ConnectionStringVariable] ?? "";
                options.UserEmail = NullIfBlank(configuration[EditorOptions.UserEmailVariable]);
                options.ChromiumPath = NullIfBlank(configuration[EditorOptions.ChromiumPathVariable]);
                options.SeedFile = NullIfBlank(configuration[EditorOptions.SeedFileVariable]);
                options.PublicHost = HostName(configuration[EditorOptions.PublicHostVariable]);
                options.AccessTeamDomain = HostName(configuration[EditorOptions.AccessTeamDomainVariable]);
                options.AccessAudience = NullIfBlank(configuration[EditorOptions.AccessAudienceVariable]);
            })
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                $"{EditorOptions.ConnectionStringVariable} is not set. Add it to .env and load it with " +
                ". ./scripts/Import-DotEnv.ps1 (see docs/cv-editor.md).")
            .Validate(options => options.RemoteAccessIsConsistent, EditorOptions.RemoteAccessIncomplete)
            .ValidateOnStart();

        // Remote access (ADR 0004): the tunnel's hostname passes host filtering too. Without it,
        // requests for that name are refused before anything else runs.
        builder.Services.AddOptions<HostFilteringOptions>()
            .PostConfigure<IOptions<EditorOptions>>((hosts, editor) =>
            {
                if (editor.Value.PublicHost is { } publicHost && !hosts.AllowedHosts.Contains(publicHost))
                {
                    hosts.AllowedHosts = [.. hosts.AllowedHosts, publicHost]; // configured as a fixed-size array
                }
            });
        builder.Services.AddSingleton<IAccessKeySource, CloudflareAccessKeySource>();
        builder.Services.AddSingleton<AccessTokenValidator>();

        builder.Services.AddDbContextFactory<CvDbContext>((services, options) =>
            CvDbContext.Configure(options, services.GetRequiredService<IOptions<EditorOptions>>().Value.ConnectionString));
        builder.Services.AddSingleton<ICvStore, CvStore>();
        builder.Services.AddSingleton<EditorWorkspace>();
        builder.Services.AddSingleton<IPdfRenderer, ChromiumPdfRenderer>();
        builder.Services.AddSingleton<PublicFiles>();

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<EditorExceptionHandler>();
        builder.Services.ConfigureHttpJsonOptions(json =>
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

        return builder;
    }

    public static WebApplication UseCvEditor(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages(); // empty error responses (e.g. a malformed body) become problem documents too
        app.UseMiddleware<EditorSecurityMiddleware>();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapEditorApi();
        return app;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>A bare host name: "https://editor.example.com/" and "editor.example.com" both give "editor.example.com".</summary>
    private static string? HostName(string? value) =>
        NullIfBlank(value) is { } trimmed
            ? (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : trimmed.TrimEnd('/')).ToLowerInvariant()
            : null;
}
