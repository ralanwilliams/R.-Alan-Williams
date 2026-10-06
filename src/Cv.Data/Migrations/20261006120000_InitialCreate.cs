using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Cv.Data.Migrations;

/// <summary>
/// Creates the cv schema. The SQL is reviewed and tested as plain SQL
/// (Migrations/Sql/*.sql, tests/db/schema-tests.sql) and runs inside EF's
/// migration transaction. CvDbContextModelSnapshot describes the resulting
/// structure so that future `dotnet ef migrations add` diffs are correct.
/// </summary>
[DbContext(typeof(CvDbContext))]
[Migration("20261006120000_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(SqlScript.Load("20261006120000_InitialCreate.up.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(SqlScript.Load("20261006120000_InitialCreate.down.sql"));
}
