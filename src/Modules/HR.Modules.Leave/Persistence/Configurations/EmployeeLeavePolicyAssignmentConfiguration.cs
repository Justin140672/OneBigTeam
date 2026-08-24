using HR.Modules.Leave.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Leave.Persistence.Configurations;

internal sealed class EmployeeLeavePolicyAssignmentConfiguration : IEntityTypeConfiguration<EmployeeLeavePolicyAssignment>
{
    public void Configure(EntityTypeBuilder<EmployeeLeavePolicyAssignment> builder)
    {
        builder.ToTable("employee_leave_policy_assignments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(a => a.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(a => a.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(a => a.LeavePolicyId)
            .HasColumnName("leave_policy_id")
            .IsRequired();

        builder.Property(a => a.EffectiveFrom)
            .HasColumnName("effective_from")
            .IsRequired();

        builder.Property(a => a.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(a => a.DeactivatedAt)
            .HasColumnName("deactivated_at");

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(a => new { a.CompanyId, a.EmployeeId })
            .IsUnique();

        builder.HasIndex(a => new { a.CompanyId, a.LeavePolicyId });

        builder.HasIndex(a => new { a.CompanyId, a.IsActive });
    }
}
