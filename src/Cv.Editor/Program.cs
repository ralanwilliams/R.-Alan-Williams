using Cv.Editor;
using Cv.Editor.Publishing;

// The CV editor. Runs on this computer only and talks to the cv schema as cv_api.
//
//   . ./scripts/Import-DotEnv.ps1
//   dotnet run --project src/Cv.Editor                          then open http://localhost:5180
//   dotnet run --project src/Cv.Editor -- backfill [--dry-run]  store files for published versions
//
// See docs/cv-editor.md for setup and docs/adr/0002-cv-editor.md for the design.

if (args is [BackfillCommand.Name, .. var options])
{
    return await BackfillCommand.RunAsync(options);
}

var builder = WebApplication.CreateBuilder(args);
builder.AddCvEditor();

var app = builder.Build();
app.UseCvEditor();
app.Run();
return 0;

/// <summary>Entry point; public so the integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
