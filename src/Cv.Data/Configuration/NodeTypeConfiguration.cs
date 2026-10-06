using Cv.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cv.Data.Configuration;

internal sealed class NodeTypeConfiguration : IEntityTypeConfiguration<NodeType>
{
    public void Configure(EntityTypeBuilder<NodeType> b)
    {
        b.ToTable("node_types");
        b.HasKey(x => x.Code).HasName("pk_node_types");

        b.Property(x => x.Code).HasColumnName("code").HasColumnType("text");
        b.Property(x => x.Description).HasColumnName("description").HasColumnType("text");
        b.Property(x => x.HasText).HasColumnName("has_text").HasColumnType("boolean");
    }
}
