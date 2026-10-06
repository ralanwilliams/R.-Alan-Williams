using Cv.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cv.Data.Configuration;

internal sealed class CvRenderConfiguration : IEntityTypeConfiguration<CvRender>
{
    public void Configure(EntityTypeBuilder<CvRender> b)
    {
        b.ToTable("cv_renders");
        b.HasKey(x => new { x.VersionId, x.LocaleCode, x.Format, x.RendererVersion }).HasName("pk_cv_renders");

        b.Property(x => x.VersionId).HasColumnName("version_id").HasColumnType("uuid");
        b.Property(x => x.LocaleCode).HasColumnName("locale").HasColumnType("text");
        b.Property(x => x.Format).HasColumnName("format").HasColumnType("text");
        b.Property(x => x.RendererVersion).HasColumnName("renderer_version").HasColumnType("text");
        b.Property(x => x.ContentHash).HasColumnName("content_hash").HasColumnType("bytea");
        b.Property(x => x.StorageKey).HasColumnName("storage_key").HasColumnType("text");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");

        b.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName("ux_cv_renders_storage_key");
        b.HasIndex(x => x.LocaleCode).HasDatabaseName("ix_cv_renders_locale");

        b.HasOne<CvVersion>().WithMany().HasForeignKey(x => x.VersionId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cv_renders_version");
        b.HasOne<Locale>().WithMany().HasForeignKey(x => x.LocaleCode)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cv_renders_locale");
    }
}
