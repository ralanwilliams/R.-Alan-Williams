using Cv.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cv.Data.Configuration;

internal sealed class CvNodeConfiguration : IEntityTypeConfiguration<CvNode>
{
    public void Configure(EntityTypeBuilder<CvNode> b)
    {
        b.ToTable("cv_nodes");
        b.HasKey(x => new { x.VersionId, x.NodeId }).HasName("pk_cv_nodes");

        b.Property(x => x.VersionId).HasColumnName("version_id").HasColumnType("uuid");
        b.Property(x => x.NodeId).HasColumnName("node_id").HasColumnType("uuid");
        b.Property(x => x.ParentNodeId).HasColumnName("parent_node_id").HasColumnType("uuid");
        b.Property(x => x.TypeCode).HasColumnName("type_code").HasColumnType("text");
        b.Property(x => x.SortKey).HasColumnName("sort_key").HasColumnType("text").UseCollation("C");
        b.Property(x => x.Attrs).HasColumnName("attrs").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        // Siblings have distinct sort keys; NULLS NOT DISTINCT so it also applies at the root level.
        b.HasIndex(x => new { x.VersionId, x.ParentNodeId, x.SortKey })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ux_cv_nodes_sibling_order");
        b.HasIndex(x => x.TypeCode).HasDatabaseName("ix_cv_nodes_type_code");

        b.HasOne<CvVersion>().WithMany().HasForeignKey(x => x.VersionId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cv_nodes_version");
        // Composite FK: the parent must be in the same version. DEFERRABLE INITIALLY DEFERRED in SQL.
        b.HasOne<CvNode>().WithMany().HasForeignKey(x => new { x.VersionId, x.ParentNodeId })
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cv_nodes_parent");
        b.HasOne<NodeType>().WithMany().HasForeignKey(x => x.TypeCode)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cv_nodes_type");
    }
}
