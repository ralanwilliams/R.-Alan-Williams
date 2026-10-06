using Cv.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cv.Data;

/// <summary>
/// EF Core context for the CV content store. Everything lives in the <c>cv</c> schema,
/// including EF's migrations history table.
/// See docs/adr/0001-cv-content-model.md for the design.
/// </summary>
public sealed class CvDbContext(DbContextOptions<CvDbContext> options) : DbContext(options)
{
    public const string Schema = "cv";
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    public DbSet<User> Users => Set<User>();
    public DbSet<Locale> Locales => Set<Locale>();
    public DbSet<NodeType> NodeTypes => Set<NodeType>();
    public DbSet<NodeTypeChild> NodeTypeChildren => Set<NodeTypeChild>();
    public DbSet<CvVersion> Versions => Set<CvVersion>();
    public DbSet<CvNode> Nodes => Set<CvNode>();
    public DbSet<CvNodeContent> NodeContents => Set<CvNodeContent>();
    public DbSet<CvPublication> Publications => Set<CvPublication>();
    public DbSet<CvRender> Renders => Set<CvRender>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    /// <summary>Builds options with the settings every caller (app, migrator, design-time tools) must share.</summary>
    public static DbContextOptions<CvDbContext> CreateOptions(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<CvDbContext>();
        Configure(builder, connectionString);
        return builder.Options;
    }

    /// <summary>Applies the shared settings to an existing builder, e.g. the one <c>AddDbContextFactory</c> passes in.</summary>
    public static void Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CvDbContext).Assembly);
    }
}
