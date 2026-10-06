using Cv.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cv.Data.Configuration;

internal sealed class CvVersionConfiguration : IEntityTypeConfiguration<CvVersion>
{
    public void Configure(EntityTypeBuilder<CvVersion> b)
    {
        b.ToTable("cv_versions");
        b.HasKey(x => x.Id).HasName("pk_cv_versions");

        b.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
        b.Property(x => x.VersionNumber).HasColumnName("version_number").HasColumnType("integer");
        b.Property(x => x.PreviousVersionId).HasColumnName("previous_version_id").HasColumnType("uuid");
        b.Property(x => x.RestoredFromVersionId).HasColumnName("restored_from_version_id").HasColumnType("uuid");
        b.Property(x => x.Summary).HasColumnName("summary").HasColumnType("text");
        b.Property(x => x.ContentHash).HasColumnName("content_hash").HasColumnType("bytea");
        b.Property(x => x.CreatedBy).HasColumnName("created_by").HasColumnType("uuid");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");

        b.HasIndex(x => x.VersionNumber).IsUnique().HasDatabaseName("ux_cv_versions_version_number");
        // At most one successor per version: no branches, and stale-tab saves are rejected.
        b.HasIndex(x => x.PreviousVersionId).IsUnique().HasDatabaseName("ux_cv_versions_previous_version_id");
        b.HasIndex(x => x.RestoredFromVersionId).HasDatabaseName("ix_cv_versions_restored_from_version_id");
        b.HasIndex(x => x.CreatedBy).HasDatabaseName("ix_cv_versions_created_by");

        b.HasOne<CvVersion>().WithMany().HasForeignKey(x => x.PreviousVersionId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cv_versions_previous_version");
        b.HasOne<CvVersion>().WithMany().HasForeignKey(x => x.RestoredFromVersionId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cv_versions_restored_from_version");
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cv_versions_created_by");
    }
}
