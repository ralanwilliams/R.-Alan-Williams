using System.Text.Json.Serialization;
using Cv.Data;
using Cv.Data.Store;
using Cv.Editor.Api;
using Cv.Editor.Pdf;
using Cv.Editor.Security;
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
            })
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                $"{EditorOptions.ConnectionStringVariable} is not set. Add it to .env and load it with " +
                ". ./scripts/Import-DotEnv.ps1 (see docs/cv-editor.md).")
            .ValidateOnStart();

        builder.Services.AddDbContextFactory<CvDbContext>((services, options) =>
            CvDbContext.Configure(options, services.GetRequiredService<IOptions<EditorOptions>>().Value.ConnectionString));
        builder.Services.AddSingleton<ICvStore, CvStore>();
        builder.Services.AddSingleton<EditorWorkspace>();
        builder.Services.AddSingleton<IPdfRenderer, ChromiumPdfRenderer>();

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
        app.UseMiddleware<LocalOnlyMiddleware>();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapEditorApi();
        return app;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
