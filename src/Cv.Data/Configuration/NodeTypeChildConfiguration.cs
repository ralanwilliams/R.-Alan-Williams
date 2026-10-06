using Cv.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cv.Data.Configuration;

internal sealed class NodeTypeChildConfiguration : IEntityTypeConfiguration<NodeTypeChild>
{
    public void Configure(EntityTypeBuilder<NodeTypeChild> b)
    {
        b.ToTable("node_type_children");
        b.HasKey(x => new { x.ParentType, x.ChildType }).HasName("pk_node_type_children");

        b.Property(x => x.ParentType).HasColumnName("parent_type").HasColumnType("text");
        b.Property(x => x.ChildType).HasColumnName("child_type").HasColumnType("text");

        b.HasIndex(x => x.ChildType).HasDatabaseName("ix_node_type_children_child_type");

        b.HasOne<NodeType>().WithMany().HasForeignKey(x => x.ParentType)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_node_type_children_parent_type");
        b.HasOne<NodeType>().WithMany().HasForeignKey(x => x.ChildType)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_node_type_children_child_type");
    }
}
