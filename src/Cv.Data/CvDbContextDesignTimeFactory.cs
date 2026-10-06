using Microsoft.EntityFrameworkCore.Design;

namespace Cv.Data;

/// <summary>
/// Used by the dotnet-ef tool. Reads the connection string from the CV_DB_CONNECTION
/// environment variable. Commands that don't touch a database (migrations add,
/// has-pending-model-changes, migrations script) work without it.
/// </summary>
public sealed class CvDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CvDbContext>
{
    public const string ConnectionStringVariable = "CV_DB_CONNECTION";

    private const string Placeholder = "Host=localhost;Database=cv_design_time_placeholder";

    public CvDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine(
                $"{ConnectionStringVariable} is not set; using a placeholder. " +
                "Commands that connect to a database will fail until you set it.");
            connectionString = Placeholder;
        }

        return new CvDbContext(CvDbContext.CreateOptions(connectionString));
    }
}
