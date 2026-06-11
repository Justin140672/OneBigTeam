using HR.Modules.Leave.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Leave.Persistence.Configurations;

internal sealed class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.ToTable("leave_requests");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(r => r.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(r => r.LeaveTypeId)
            .HasColumnName("leave_type_id")
            .IsRequired();

        builder.Property(r => r.LeavePolicyId)
            .HasColumnName("leave_policy_id");

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.StartDate)
            .HasColumnName("start_date")
            .IsRequired();

        builder.Property(r => r.EndDate)
            .HasColumnName("end_date")
            .IsRequired();

        builder.Property(r => r.TotalDays)
            .HasColumnName("total_days")
            .HasColumnType("numeric(6,2)")
            .IsRequired();

        builder.Property(r => r.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        builder.Property(r => r.ReviewedByEmployeeId)
            .HasColumnName("reviewed_by_employee_id");

        builder.Property(r => r.ReviewedAt)
            .HasColumnName("reviewed_at");

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(r => r.CompanyId);
        builder.HasIndex(r => r.EmployeeId);
        builder.HasIndex(r => r.LeaveTypeId);
        builder.HasIndex(r => new { r.CompanyId, r.EmployeeId });
        builder.HasIndex(r => new { r.CompanyId, r.Status });
    }
}
