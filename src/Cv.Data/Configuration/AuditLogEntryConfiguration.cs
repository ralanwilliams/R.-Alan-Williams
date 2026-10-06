using Cv.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cv.Data.Configuration;

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> b)
    {
        b.ToTable("audit_log");
        b.HasKey(x => x.Id).HasName("pk_audit_log");

        b.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");
        b.Property(x => x.Action).HasColumnName("action").HasColumnType("text");
        b.Property(x => x.SubjectId).HasColumnName("subject_id").HasColumnType("uuid");
        b.Property(x => x.Details).HasColumnName("details").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        b.Property(x => x.ActorId).HasColumnName("actor_id").HasColumnType("uuid");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");

        b.HasIndex(x => x.ActorId).HasDatabaseName("ix_audit_log_actor_id");
        b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_audit_log_created_at");

        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_audit_log_actor");
    }
}
