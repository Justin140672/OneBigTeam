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

        // IAM-04: company_id is required on every tenant-owned table per the database standards
        // (05-database-standards.md) — also lets override administration/search/reporting (IAM-05,
        // IAM-08) filter by company without joining through UserProfile.
        builder.Property(e => e.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.HasIndex(e => e.CompanyId)
            .HasDatabaseName("ix_employee_role_overrides_company_id");

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

        builder.Property(e => e.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired(false);

        builder.Property(e => e.AssignedAt)
            .HasColumnName("assigned_at")
            .IsRequired();

        builder.Property(e => e.AssignedBy)
            .HasColumnName("assigned_by");

        // A user may only have one override per role (Grant or Deny, not both).
        builder.HasIndex(e => new { e.UserId, e.RoleId })
            .IsUnique()
            .HasDatabaseName("ix_employee_role_overrides_user_role");

        // IAM-04: no FK constraint on UserId, matching UserRoleConfiguration/UserPositionConfiguration's
        // precedent — UserId is the owning Employee's id (ApplicationUser.Id == EmployeeId by
        // convention) and overrides may need to be administered for a user id before an
        // ApplicationUser row necessarily exists for every code path that could set one.

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
