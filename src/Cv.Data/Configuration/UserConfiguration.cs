using Cv.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cv.Data.Configuration;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id).HasName("pk_users");

        b.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
        b.Property(x => x.Email).HasColumnName("email").HasColumnType("text");
        b.Property(x => x.DisplayName).HasColumnName("display_name").HasColumnType("text");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");

        // Case-insensitive unique email (ux_users_email_ci) is an expression index: see the migration SQL.
    }
}
