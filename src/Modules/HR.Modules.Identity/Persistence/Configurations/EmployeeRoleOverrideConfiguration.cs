using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class EmployeeRoleOverrideConfiguration : IEntityTypeConfiguration<EmployeeRoleOverride>
{
    public void Configure(EntityTypeBuilder<EmployeeRoleOverride> builder)
    {
        builder.ToTable("employee_role_overrides");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(e => e.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        builder.Property(e => e.OverrideType)
            .HasColumnName("override_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.AssignedAt)
            .HasColumnName("assigned_at")
            .IsRequired();

        builder.Property(e => e.AssignedBy)
            .HasColumnName("assigned_by");

        // A user may only have one override per role (Grant or Deny, not both).
        builder.HasIndex(e => new { e.UserId, e.RoleId })
            .IsUnique()
            .HasDatabaseName("ix_employee_role_overrides_user_role");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
