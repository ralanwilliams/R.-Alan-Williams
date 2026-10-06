using Microsoft.EntityFrameworkCore.Migrations;

namespace Cv.Data.Migrations;

/// <summary>
/// Public CV downloads (ADR 0003 §1, §3): <c>cv_renders</c> stores the rendered file itself
/// instead of a key into object storage, and the <c>cv_public</c> role may call one function,
/// <c>cv.cv_public_render</c>, which returns only versions that were published for the locale.
/// The SQL is reviewed as plain SQL in Migrations/Sql.
/// </summary>
public partial class StoreRenderContent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(SqlScript.Load("20261006165920_StoreRenderContent.up.sql"));

    // Fails (by design) once any file has been stored.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(SqlScript.Load("20261006165920_StoreRenderContent.down.sql"));
}
