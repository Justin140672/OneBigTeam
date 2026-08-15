using HR.Modules.Companies.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Companies.Persistence.Configurations;

internal sealed class PlatformMetricsSnapshotConfiguration : IEntityTypeConfiguration<PlatformMetricsSnapshot>
{
    public void Configure(EntityTypeBuilder<PlatformMetricsSnapshot> builder)
    {
        builder.ToTable("platform_metrics_snapshots");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(s => s.SnapshotDate)
            .HasColumnName("snapshot_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(s => s.ComputedAt)
            .HasColumnName("computed_at")
            .IsRequired();

        builder.Property(s => s.ActiveCompanies)
            .HasColumnName("active_companies")
            .IsRequired();

        builder.Property(s => s.ActiveUsers)
            .HasColumnName("active_users")
            .IsRequired();

        builder.Property(s => s.StorageConsumedBytes)
            .HasColumnName("storage_consumed_bytes")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(s => s.BackgroundJobsSucceededTotal)
            .HasColumnName("background_jobs_succeeded_total")
            .IsRequired();

        builder.HasIndex(s => s.SnapshotDate)
            .IsUnique()
            .HasDatabaseName("ix_platform_metrics_snapshots_snapshot_date");
    }
}
