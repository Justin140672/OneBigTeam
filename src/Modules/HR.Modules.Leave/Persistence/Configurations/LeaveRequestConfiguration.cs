using HR.Modules.Leave.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Leave.Persistence.Configurations;

internal sealed class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.ToTable("leave_requests");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(l => l.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(l => l.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(l => l.LeaveType)
            .HasColumnName("leave_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(l => l.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(l => l.StartDate)
            .HasColumnName("start_date")
            .IsRequired();

        builder.Property(l => l.EndDate)
            .HasColumnName("end_date")
            .IsRequired();

        builder.Property(l => l.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        builder.Property(l => l.ReviewedByEmployeeId)
            .HasColumnName("reviewed_by_employee_id");

        builder.Property(l => l.ReviewedAt)
            .HasColumnName("reviewed_at");

        builder.Property(l => l.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(l => l.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(l => l.CompanyId);
        builder.HasIndex(l => l.EmployeeId);
        builder.HasIndex(l => new { l.CompanyId, l.EmployeeId });
        builder.HasIndex(l => new { l.CompanyId, l.Status });
    }
}
