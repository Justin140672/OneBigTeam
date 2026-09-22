using HR.Modules.DataImport.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.DataImport.Persistence.Configurations;

internal sealed class OrphanedImportFileUploadConfiguration : IEntityTypeConfiguration<OrphanedImportFileUpload>
{
    public void Configure(EntityTypeBuilder<OrphanedImportFileUpload> builder)
    {
        builder.ToTable("orphaned_import_file_uploads");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(o => o.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(o => o.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(o => o.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(o => o.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(o => o.LastAttemptedAt)
            .HasColumnName("last_attempted_at");

        builder.Property(o => o.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(o => o.ConfirmedAt)
            .HasColumnName("confirmed_at");

        builder.Property(o => o.ClearedAt)
            .HasColumnName("cleared_at");

        builder.HasIndex(o => o.CompanyId);
        builder.HasIndex(o => o.DeletedAt);
        builder.HasIndex(o => new { o.ConfirmedAt, o.DeletedAt, o.ClearedAt, o.CreatedAt });
    }
}
