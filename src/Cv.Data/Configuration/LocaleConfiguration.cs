using Cv.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cv.Data.Configuration;

internal sealed class LocaleConfiguration : IEntityTypeConfiguration<Locale>
{
    public void Configure(EntityTypeBuilder<Locale> b)
    {
        b.ToTable("locales");
        b.HasKey(x => x.Code).HasName("pk_locales");

        // Principal key for cv_publications' composite FK, which only matches publishable locales.
        b.HasAlternateKey(x => new { x.Code, x.IsPublishable }).HasName("ak_locales_code_is_publishable");

        b.Property(x => x.Code).HasColumnName("code").HasColumnType("text");
        b.Property(x => x.Name).HasColumnName("name").HasColumnType("text");
        b.Property(x => x.IsSource).HasColumnName("is_source").HasColumnType("boolean");
        b.Property(x => x.IsPublishable).HasColumnName("is_publishable").HasColumnType("boolean");
    }
}
