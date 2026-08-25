using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Employees.Persistence.Configurations;

internal sealed class EmployeeLeavingProcessConfiguration : IEntityTypeConfiguration<EmployeeLeavingProcess>
{
    public void Configure(EntityTypeBuilder<EmployeeLeavingProcess> builder)
    {
        builder.ToTable("employee_leaving_processes");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(p => p.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(p => p.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(p => p.ResignationReceivedDate)
            .HasColumnName("resignation_received_date")
            .IsRequired();

        builder.Property(p => p.LeavingDate)
            .HasColumnName("leaving_date")
            .IsRequired();

        builder.Property(p => p.LastWorkingDay)
            .HasColumnName("last_working_day")
            .IsRequired();

        builder.Property(p => p.NoticePeriodUnit)
            .HasColumnName("notice_period_unit")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.NoticePeriodLength)
            .HasColumnName("notice_period_length")
            .IsRequired();

        builder.Property(p => p.NoticeSource)
            .HasColumnName("notice_source")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.LeavingReason)
            .HasColumnName("leaving_reason")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.StartedAt)
            .HasColumnName("started_at")
            .IsRequired();

        builder.Property(p => p.StartedByUserId)
            .HasColumnName("started_by_user_id")
            .IsRequired();

        builder.Property(p => p.CancelledAt)
            .HasColumnName("cancelled_at");

        builder.Property(p => p.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(2000);

        builder.Property(p => p.ReplacementManagerEmployeeId)
            .HasColumnName("replacement_manager_employee_id");

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(p => p.CompanyId);
        builder.HasIndex(p => new { p.CompanyId, p.EmployeeId });
        builder.HasIndex(p => new { p.CompanyId, p.EmployeeId, p.Status });
    }
}
