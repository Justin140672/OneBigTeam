using HR.Modules.Sickness.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Sickness.Persistence.Configurations;

internal sealed class SicknessEvidenceRequestConfiguration : IEntityTypeConfiguration<SicknessEvidenceRequest>
{
    public void Configure(EntityTypeBuilder<SicknessEvidenceRequest> builder)
    {
        builder.ToTable("sickness_evidence_requests");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(r => r.SicknessRecordId)
            .HasColumnName("sickness_record_id")
            .IsRequired();

        builder.Property(r => r.RequestedAt)
            .HasColumnName("requested_at")
            .IsRequired();

        builder.Property(r => r.RequestedBy)
            .HasColumnName("requested_by")
            .IsRequired();

        builder.Property(r => r.DueDate)
            .HasColumnName("due_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(r => r.Notes)
            .HasColumnName("notes")
            .HasMaxLength(2000);

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(r => r.FulfilledAt)
            .HasColumnName("fulfilled_at");

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasOne<SicknessRecord>()
            .WithMany()
            .HasForeignKey(r => r.SicknessRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.CompanyId);
        builder.HasIndex(r => r.SicknessRecordId);
        builder.HasIndex(r => new { r.CompanyId, r.Status });
    }
}
