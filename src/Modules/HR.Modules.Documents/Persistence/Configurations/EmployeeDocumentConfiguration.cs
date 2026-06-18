using HR.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Documents.Persistence.Configurations;

internal sealed class EmployeeDocumentConfiguration : IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeDocument> builder)
    {
        builder.ToTable("employee_documents");

        builder.HasKey(ed => ed.Id);

        builder.Property(ed => ed.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(ed => ed.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(ed => ed.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(ed => ed.DocumentId)
            .HasColumnName("document_id")
            .IsRequired();

        builder.Property(ed => ed.AddedBy)
            .HasColumnName("added_by")
            .IsRequired();

        builder.Property(ed => ed.IssueDate)
            .HasColumnName("issue_date");

        builder.Property(ed => ed.ExpiryDate)
            .HasColumnName("expiry_date");

        builder.Property(ed => ed.AcknowledgedAt)
            .HasColumnName("acknowledged_at");

        builder.Property(ed => ed.ExpiringSoonNotifiedAt)
            .HasColumnName("expiring_soon_notified_at");

        builder.Property(ed => ed.ExpiredNotifiedAt)
            .HasColumnName("expired_notified_at");

        builder.Property(ed => ed.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(ed => ed.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(ed => ed.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ed => new { ed.CompanyId, ed.EmployeeId });
        builder.HasIndex(ed => new { ed.EmployeeId, ed.DocumentId }).IsUnique();
    }
}
