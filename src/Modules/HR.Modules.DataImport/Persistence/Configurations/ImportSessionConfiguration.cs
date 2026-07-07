using HR.Modules.DataImport.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.DataImport.Persistence.Configurations;

internal sealed class ImportSessionConfiguration : IEntityTypeConfiguration<ImportSession>
{
    public void Configure(EntityTypeBuilder<ImportSession> builder)
    {
        builder.ToTable("import_sessions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(s => s.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(s => s.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(260)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.TotalRows)
            .HasColumnName("total_rows")
            .IsRequired();

        builder.Property(s => s.ProcessedRows)
            .HasColumnName("processed_rows")
            .IsRequired();

        builder.Property(s => s.SuccessfulRows)
            .HasColumnName("successful_rows")
            .IsRequired();

        builder.Property(s => s.FailedRows)
            .HasColumnName("failed_rows")
            .IsRequired();

        builder.Property(s => s.InitiatedByUserId)
            .HasColumnName("initiated_by_user_id")
            .IsRequired();

        builder.Property(s => s.StartedAt)
            .HasColumnName("started_at");

        builder.Property(s => s.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(s => s.ErrorSummary)
            .HasColumnName("error_summary")
            .HasMaxLength(4000);

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(s => s.CompanyId);
        builder.HasIndex(s => s.Status);
    }
}
