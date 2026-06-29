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

        builder.Property(r => r.PositionProfileRequiredDocumentId)
            .HasColumnName("position_profile_required_document_id");

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.DueDate)
            .HasColumnName("due_date");

        builder.Property(r => r.RequestedByEmployeeId)
            .HasColumnName("requested_by_employee_id");

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(r => r.CompletedByEmployeeId)
            .HasColumnName("completed_by_employee_id");

        builder.HasIndex(r => new { r.CompanyId, r.EmployeeId });
        builder.HasIndex(r => new { r.EmployeeId, r.DocumentTypeId }).IsUnique();
    }
}
