using HR.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Documents.Persistence.Configurations;

internal sealed class DocumentRequestConfiguration : IEntityTypeConfiguration<DocumentRequest>
{
    public void Configure(EntityTypeBuilder<DocumentRequest> builder)
    {
        builder.ToTable("document_requests");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(r => r.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(r => r.DocumentTypeId)
            .HasColumnName("document_type_id")
            .IsRequired();

        builder.Property(r => r.IsMandatory)
            .HasColumnName("is_mandatory")
            .IsRequired();

        builder.Property(r => r.DueDaysAfterStart)
            .HasColumnName("due_days_after_start");

        builder.Property(r => r.RequiresExpiryDate)
            .HasColumnName("requires_expiry_date")
            .IsRequired();

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.FulfilledAt)
            .HasColumnName("fulfilled_at");

        builder.HasIndex(r => new { r.CompanyId, r.EmployeeId });
        builder.HasIndex(r => new { r.EmployeeId, r.DocumentTypeId }).IsUnique();
    }
}
