using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Employees.Persistence.Configurations;

internal sealed class PendingManagerChangedEventConfiguration : IEntityTypeConfiguration<PendingManagerChangedEvent>
{
    public void Configure(EntityTypeBuilder<PendingManagerChangedEvent> builder)
    {
        builder.ToTable("pending_manager_changed_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(e => e.ReportEmployeeId)
            .HasColumnName("report_employee_id")
            .IsRequired();

        builder.Property(e => e.PreviousManagerId)
            .HasColumnName("previous_manager_id");

        builder.Property(e => e.NewManagerId)
            .HasColumnName("new_manager_id");

        builder.Property(e => e.LeavingProcessId)
            .HasColumnName("leaving_process_id")
            .IsRequired();

        builder.Property(e => e.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.PublishedAt)
            .HasColumnName("published_at");

        builder.HasIndex(e => e.CompanyId);

        // Supports ReconcilePendingManagerChangedEventsJob's scan for records still awaiting
        // publication.
        builder.HasIndex(e => e.PublishedAt);
    }
}
