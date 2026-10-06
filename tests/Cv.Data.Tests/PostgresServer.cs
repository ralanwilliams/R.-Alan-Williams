using Cv.Data.Store;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Cv.Data.Tests.PostgresServer))]

namespace Cv.Data.Tests;

/// <summary>
/// One PostgreSQL server for the whole test run. The migration is applied once to a template
/// database; every test then gets its own copy (<c>CREATE DATABASE ... TEMPLATE</c>), because
/// the schema is append-only and tests could not clean up after each other otherwise.
/// </summary>
public sealed class PostgresServer : IAsyncLifetime
{
    public const string ConnectionVariable = "CV_TEST_POSTGRES";
    public const string RequireVariable = "CV_TEST_REQUIRE_DATABASE";

    /// <summary>The seeded author, as created in docs/cv-database.md §6.</summary>
    public static readonly Guid UserId = Guid.Parse("0199a0a0-0000-7000-8000-000000000001");

    private const string TemplateDatabase = "cv_test_template";

    // A login in cv_app, like the cv_api role in production: tests run with the app's real privileges.
    private const string AppRole = "cv_test_app";
    private const string AppPassword = "cv_test_app";

    private readonly List<string> _databases = [];
    private PostgreSqlContainer? _container;
    private string? _adminConnectionString;

    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        _adminConnectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (_adminConnectionString is null)
        {
            try
            {
                _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
                await _container.StartAsync();
                _adminConnectionString = _container.GetConnectionString();
            }
            catch (Exception e) when (Environment.GetEnvironmentVariable(RequireVariable) != "1")
            {
                SkipReason = $"No PostgreSQL available: set {ConnectionVariable} or start Docker. ({e.GetType().Name}: {e.Message})";
                return;
            }
        }

        await ExecuteAsync(_adminConnectionString,
            $"DROP DATABASE IF EXISTS {TemplateDatabase} WITH (FORCE)",
            $"CREATE DATABASE {TemplateDatabase}");

        var template = WithDatabase(_adminConnectionString, TemplateDatabase);
        await using (var db = new CvDbContext(CvDbContext.CreateOptions(template)))
        {
            await db.Database.MigrateAsync();
        }
        await ExecuteAsync(template,
            $"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                    CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}' IN ROLE cv_app;
                END IF;
            END $$
            """,
            $"INSERT INTO cv.users (id, email, display_name) VALUES ('{UserId}', 'ada@example.com', 'Ada Lovelace')");

        NpgsqlConnection.ClearAllPools(); // a template database must have no open connections
    }

    /// <summary>A fresh, migrated database with one user. Skips the test when no server is available.</summary>
    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        if (SkipReason is not null)
        {
            Assert.Skip(SkipReason);
        }

        var name = $"cv_test_{Guid.NewGuid():N}"[..24];
        await ExecuteAsync(_adminConnectionString!, $"CREATE DATABASE {name} TEMPLATE {TemplateDatabase}");
        lock (_databases)
        {
            _databases.Add(name);
        }

        var admin = WithDatabase(_adminConnectionString!, name);
        var app = new NpgsqlConnectionStringBuilder(admin) { Username = AppRole, Password = AppPassword }.ConnectionString;
        return new TestDatabase(admin, app);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync(); // takes every database with it
            return;
        }
        if (_adminConnectionString is null || SkipReason is not null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync(_adminConnectionString,
            [.. _databases.Append(TemplateDatabase).Select(d => $"DROP DATABASE IF EXISTS {d} WITH (FORCE)")]);
    }

    private static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, params string[] statements)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var sql in statements)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}

/// <param name="AdminConnectionString">The schema owner, for checks the app role may not do.</param>
/// <param name="AppConnectionString">A member of cv_app, as the editor connects in production.</param>
public sealed record TestDatabase(string AdminConnectionString, string AppConnectionString)
{
    /// <summary>A store with empty caches, as after an editor restart.</summary>
    public CvStore CreateStore() => new(new ContextFactory(AppConnectionString));

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CvDbContext>
    {
        public CvDbContext CreateDbContext() => new(CvDbContext.CreateOptions(connectionString));
    }
}
