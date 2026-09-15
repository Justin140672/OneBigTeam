using HR.Modules.Leave.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Leave.Persistence.Configurations;

internal sealed class HistoricalLeaveDeactivationRepairProgressConfiguration
    : IEntityTypeConfiguration<HistoricalLeaveDeactivationRepairProgress>
{
    public void Configure(EntityTypeBuilder<HistoricalLeaveDeactivationRepairProgress> builder)
    {
        builder.ToTable("historical_leave_deactivation_repair_progress");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(p => p.LastProcessedFinalisationCompletedAt)
            .HasColumnName("last_processed_finalisation_completed_at");

        builder.Property(p => p.LastProcessedEmployeeId)
            .HasColumnName("last_processed_employee_id");

        builder.Property(p => p.IsComplete)
            .HasColumnName("is_complete")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(p => p.TotalRepaired)
            .HasColumnName("total_repaired")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}
