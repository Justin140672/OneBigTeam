using HR.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Documents.Persistence.Configurations;

internal sealed class FileScanWorkConfiguration : IEntityTypeConfiguration<FileScanWork>
{
    public void Configure(EntityTypeBuilder<FileScanWork> builder)
    {
        builder.ToTable("file_scan_work");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(w => w.TargetType).HasColumnName("target_type").HasConversion<int>().IsRequired();
        builder.Property(w => w.EntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(w => w.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(w => w.State).HasColumnName("state").HasConversion<int>().IsRequired();
        builder.Property(w => w.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(w => w.DispatchCount).HasColumnName("dispatch_count").IsRequired();
        builder.Property(w => w.LeaseToken).HasColumnName("lease_token");
        builder.Property(w => w.LeaseExpiresAt).HasColumnName("lease_expires_at");
        builder.Property(w => w.NextAttemptAt).HasColumnName("next_attempt_at").IsRequired();
        builder.Property(w => w.LastDispatchedAt).HasColumnName("last_dispatched_at");
        builder.Property(w => w.LastError).HasColumnName("last_error").HasMaxLength(256);
        builder.Property(w => w.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(w => w.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(w => w.CompletedAt).HasColumnName("completed_at");
        builder.Property(w => w.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.HasIndex(w => new { w.TargetType, w.EntityId }).IsUnique()
            .HasDatabaseName("ux_file_scan_work_target_entity");
        builder.HasIndex(w => new { w.State, w.NextAttemptAt }).HasDatabaseName("ix_file_scan_work_state_next_attempt_at");
        builder.HasIndex(w => new { w.State, w.LeaseExpiresAt }).HasDatabaseName("ix_file_scan_work_state_lease_expires_at");
    }
}
