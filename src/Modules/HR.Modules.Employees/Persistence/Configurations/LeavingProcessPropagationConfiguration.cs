using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Employees.Persistence.Configurations;

internal sealed class LeavingProcessPropagationConfiguration : IEntityTypeConfiguration<LeavingProcessPropagation>
{
    public void Configure(EntityTypeBuilder<LeavingProcessPropagation> builder)
    {
        builder.ToTable("leaving_process_propagations");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(e => e.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(e => e.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.Property(e => e.LeavingProcessId).HasColumnName("leaving_process_id").IsRequired();
        builder.Property(e => e.OperationType).HasColumnName("operation_type").HasMaxLength(32).IsRequired();
        builder.Property(e => e.LeavingDate).HasColumnName("leaving_date");
        builder.Property(e => e.LastWorkingDay).HasColumnName("last_working_day");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(e => e.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(e => e.LastError).HasColumnName("last_error").HasMaxLength(1000);
        builder.Property(e => e.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(e => e.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id");
        builder.Property(e => e.CausationId).HasColumnName("causation_id");

        builder.Ignore(e => e.IsCancellation);

        builder.HasIndex(e => new { e.CompanyId, e.EmployeeId, e.CreatedAt });
        builder.HasIndex(e => new { e.Status, e.NextAttemptAt });
        builder.HasIndex(e => e.LeavingProcessId);
    }
}
