using Cv.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cv.Data.Configuration;

internal sealed class CvNodeContentConfiguration : IEntityTypeConfiguration<CvNodeContent>
{
    public void Configure(EntityTypeBuilder<CvNodeContent> b)
    {
        b.ToTable("cv_node_contents");
        b.HasKey(x => new { x.VersionId, x.NodeId, x.LocaleCode }).HasName("pk_cv_node_contents");

        b.Property(x => x.VersionId).HasColumnName("version_id").HasColumnType("uuid");
        b.Property(x => x.NodeId).HasColumnName("node_id").HasColumnType("uuid");
        b.Property(x => x.LocaleCode).HasColumnName("locale").HasColumnType("text");
        b.Property(x => x.Content).HasColumnName("content").HasColumnType("text");
        b.Property(x => x.IsOmitted).HasColumnName("is_omitted").HasColumnType("boolean");
        b.Property(x => x.SourceHash).HasColumnName("source_hash").HasColumnType("bytea");

        b.HasIndex(x => x.LocaleCode).HasDatabaseName("ix_cv_node_contents_locale");

        b.HasOne<CvNode>().WithMany().HasForeignKey(x => new { x.VersionId, x.NodeId })
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cv_node_contents_node");
        b.HasOne<Locale>().WithMany().HasForeignKey(x => x.LocaleCode)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cv_node_contents_locale");
    }
}
