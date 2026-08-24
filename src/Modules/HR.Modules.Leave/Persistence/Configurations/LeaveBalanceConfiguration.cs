using HR.Modules.Leave.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Leave.Persistence.Configurations;

internal sealed class LeaveBalanceConfiguration : IEntityTypeConfiguration<LeaveBalance>
{
    public void Configure(EntityTypeBuilder<LeaveBalance> builder)
    {
        builder.ToTable("leave_balances");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(b => b.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(b => b.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(b => b.LeaveTypeId)
            .HasColumnName("leave_type_id")
            .IsRequired();

        builder.Property(b => b.LeavePolicyId)
            .HasColumnName("leave_policy_id")
            .IsRequired();

        builder.Property(b => b.PolicyYear)
            .HasColumnName("policy_year")
            .IsRequired();

        builder.HasIndex(b => new { b.CompanyId, b.EmployeeId, b.LeaveTypeId, b.PolicyYear })
            .IsUnique();

        builder.Property(b => b.EntitlementDays)
            .HasColumnName("entitlement_days")
            .HasColumnType("numeric(6,2)")
            .IsRequired();

        builder.Property(b => b.UsedDays)
            .HasColumnName("used_days")
            .HasColumnType("numeric(6,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(b => b.AdjustmentDays)
            .HasColumnName("adjustment_days")
            .HasColumnType("numeric(6,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(b => b.AccrualStartDate)
            .HasColumnName("accrual_start_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Ignore(b => b.RemainingDays);

        builder.Property(b => b.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(b => b.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(b => new { b.CompanyId, b.EmployeeId });
        builder.HasIndex(b => new { b.CompanyId, b.LeaveTypeId });
    }
}
