using HR.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Documents.Persistence.Configurations;

internal sealed class SharedCompanyDocumentAcknowledgementConfiguration : IEntityTypeConfiguration<SharedCompanyDocumentAcknowledgement>
{
    public void Configure(EntityTypeBuilder<SharedCompanyDocumentAcknowledgement> builder)
    {
        builder.ToTable("shared_company_document_acknowledgements");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(a => a.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(a => a.SharedCompanyDocumentId)
            .HasColumnName("shared_company_document_id")
            .IsRequired();

        builder.Property(a => a.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(a => a.VersionNumber)
            .HasColumnName("version_number")
            .IsRequired();

        builder.Property(a => a.AcknowledgementStatement)
            .HasColumnName("acknowledgement_statement")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(a => a.AcknowledgedAt)
            .HasColumnName("acknowledged_at")
            .IsRequired();

        builder.Property(a => a.TaskId)
            .HasColumnName("task_id");

        builder.Property(a => a.IsConfirmed)
            .HasColumnName("is_confirmed")
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasOne<SharedCompanyDocument>()
            .WithMany()
            .HasForeignKey(a => a.SharedCompanyDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.SharedCompanyDocumentId, a.EmployeeId, a.VersionNumber }).IsUnique();
        builder.HasIndex(a => a.CompanyId);
    }
}
