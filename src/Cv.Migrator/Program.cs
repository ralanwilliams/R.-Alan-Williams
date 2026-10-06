using Cv.Data;
using Microsoft.EntityFrameworkCore;

// Applies pending EF Core migrations to the database in CV_DB_CONNECTION.
//
//   dotnet run --project src/Cv.Migrator              apply pending migrations
//   dotnet run --project src/Cv.Migrator -- --list    show applied and pending, change nothing
//
// It is also the startup project for the dotnet-ef tool (see docs/cv-database.md).

var connectionString = Environment.GetEnvironmentVariable(CvDbContextDesignTimeFactory.ConnectionStringVariable);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"Set {CvDbContextDesignTimeFactory.ConnectionStringVariable} to the database connection string first.");
    return 1;
}

var listOnly = args.Contains("--list");

await using var db = new CvDbContext(CvDbContext.CreateOptions(connectionString));

var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

Console.WriteLine($"Applied ({applied.Count}):");
applied.ForEach(m => Console.WriteLine($"  {m}"));
Console.WriteLine($"Pending ({pending.Count}):");
pending.ForEach(m => Console.WriteLine($"  {m}"));

if (listOnly || pending.Count == 0)
{
    Console.WriteLine(pending.Count == 0 ? "Database is up to date." : "List only; nothing applied.");
    return 0;
}

Console.WriteLine("Applying...");
await db.Database.MigrateAsync();
Console.WriteLine("Done.");
return 0;
