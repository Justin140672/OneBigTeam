using HR.Modules.Leave.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Leave.Persistence.Configurations;

internal sealed class LeaveBalanceAdjustmentConfiguration : IEntityTypeConfiguration<LeaveBalanceAdjustment>
{
    public void Configure(EntityTypeBuilder<LeaveBalanceAdjustment> builder)
    {
        builder.ToTable("leave_balance_adjustments");

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

        builder.Property(a => a.LeaveTypeId)
            .HasColumnName("leave_type_id")
            .IsRequired();

        builder.Property(a => a.AdjustmentHours)
            .HasColumnName("adjustment_hours")
            .HasColumnType("numeric(6,2)")
            .IsRequired();

        builder.Property(a => a.Reason)
            .HasColumnName("reason")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.Comments)
            .HasColumnName("comments")
            .HasMaxLength(500);

        builder.Property(a => a.AdjustedByEmployeeId)
            .HasColumnName("adjusted_by_employee_id")
            .IsRequired();

        builder.Property(a => a.AdjustedAt)
            .HasColumnName("adjusted_at")
            .IsRequired();

        builder.HasIndex(a => new { a.CompanyId, a.EmployeeId, a.LeaveTypeId, a.AdjustedAt });
        builder.HasIndex(a => new { a.CompanyId, a.EmployeeId });
    }
}
