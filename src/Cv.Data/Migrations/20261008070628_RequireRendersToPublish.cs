using Microsoft.EntityFrameworkCore.Migrations;

namespace Cv.Data.Migrations;

/// <summary>
/// Publishing requires stored files (ADR 0003 §2): the publish trigger refuses a version for a
/// locale until <c>cv_renders</c> holds its file in every format. Only new publication rows are
/// checked; the editor's backfill command stores files for versions published before this.
/// The SQL is reviewed as plain SQL in Migrations/Sql.
/// </summary>
public partial class RequireRendersToPublish : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(SqlScript.Load("20261008070628_RequireRendersToPublish.up.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(SqlScript.Load("20261008070628_RequireRendersToPublish.down.sql"));
}
