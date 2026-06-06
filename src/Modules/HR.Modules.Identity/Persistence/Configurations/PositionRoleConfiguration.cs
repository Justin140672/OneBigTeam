using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class PositionRoleConfiguration : IEntityTypeConfiguration<PositionRole>
{
    public void Configure(EntityTypeBuilder<PositionRole> builder)
    {
        builder.ToTable("position_roles");

        builder.HasKey(pr => new { pr.PositionId, pr.RoleId });

        builder.Property(pr => pr.PositionId)
            .HasColumnName("position_id")
            .IsRequired();

        builder.Property(pr => pr.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        builder.Property(pr => pr.AssignedAt)
            .HasColumnName("assigned_at")
            .IsRequired();

        builder.HasOne<Position>()
            .WithMany()
            .HasForeignKey(pr => pr.PositionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(pr => pr.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
