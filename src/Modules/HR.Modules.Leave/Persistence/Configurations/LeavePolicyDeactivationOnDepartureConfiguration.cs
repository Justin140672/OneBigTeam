using HR.Modules.Leave.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Leave.Persistence.Configurations;

internal sealed class LeavePolicyDeactivationOnDepartureConfiguration : IEntityTypeConfiguration<LeavePolicyDeactivationOnDeparture>
{
    public void Configure(EntityTypeBuilder<LeavePolicyDeactivationOnDeparture> builder)
    {
        builder.ToTable("leave_policy_deactivations_on_departure");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(d => d.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(d => d.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(d => d.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();

        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(d => d.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(d => d.LastAttemptAt)
            .HasColumnName("last_attempt_at");

        builder.Property(d => d.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(500);

        builder.Property(d => d.RequestedAt)
            .HasColumnName("requested_at")
            .IsRequired();

        builder.Property(d => d.ProcessedAt)
            .HasColumnName("processed_at");

        builder.HasIndex(d => d.CompanyId);
        builder.HasIndex(d => new { d.Status, d.RequestedAt });

        // At most one durable deactivation record per (company, employee) — repeated delivery of
        // the same (or a reconciliation-republished) EmployeeDepartureFinalisedIntegrationEvent must
        // never enqueue a second deactivation attempt for the same employee's assignment.
        builder.HasIndex(d => new { d.CompanyId, d.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ix_leave_policy_deactivations_on_departure_company_employee");
    }
}
