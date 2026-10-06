using Cv.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cv.Data.Configuration;

internal sealed class CvPublicationConfiguration : IEntityTypeConfiguration<CvPublication>
{
    public void Configure(EntityTypeBuilder<CvPublication> b)
    {
        b.ToTable("cv_publications");
        b.HasKey(x => x.Seq).HasName("pk_cv_publications");

        b.Property(x => x.Seq).HasColumnName("seq").HasColumnType("bigint").UseIdentityAlwaysColumn();
        b.Property(x => x.LocaleCode).HasColumnName("locale").HasColumnType("text");
        b.Property(x => x.LocalePublishable).HasColumnName("locale_publishable").HasColumnType("boolean");
        b.Property(x => x.VersionId).HasColumnName("version_id").HasColumnType("uuid");
        b.Property(x => x.PublishedBy).HasColumnName("published_by").HasColumnType("uuid");
        b.Property(x => x.PublishedAt).HasColumnName("published_at").HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");
        b.Property(x => x.Note).HasColumnName("note").HasColumnType("text");

        b.HasIndex(x => new { x.LocaleCode, x.LocalePublishable, x.Seq })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_cv_publications_locale_history");
        b.HasIndex(x => x.VersionId).HasDatabaseName("ix_cv_publications_version_id");
        b.HasIndex(x => x.PublishedBy).HasDatabaseName("ix_cv_publications_published_by");

        // Only (code, true) rows exist for publishable locales, so 'zxx' can never be published.
        b.HasOne<Locale>().WithMany()
            .HasForeignKey(x => new { x.LocaleCode, x.LocalePublishable })
            .HasPrincipalKey(x => new { x.Code, x.IsPublishable })
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cv_publications_locale");
        b.HasOne<CvVersion>().WithMany().HasForeignKey(x => x.VersionId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cv_publications_version");
        b.HasOne<User>().WithMany().HasForeignKey(x => x.PublishedBy)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cv_publications_published_by");
    }
}
