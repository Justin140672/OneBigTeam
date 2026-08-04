using HR.Modules.Support.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Support.Persistence.Configurations;

internal sealed class SupportRequestConfiguration : IEntityTypeConfiguration<SupportRequest>
{
    public void Configure(EntityTypeBuilder<SupportRequest> builder)
    {
        builder.ToTable("support_requests");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(r => r.SubmittedByUserId)
            .HasColumnName("submitted_by_user_id")
            .IsRequired();

        builder.Property(r => r.SubmittedByEmployeeId)
            .HasColumnName("submitted_by_employee_id");

        builder.Property(r => r.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(r => r.Description)
            .HasColumnName("description")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(r => r.Priority)
            .HasColumnName("priority")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.ReferenceNumber)
            .HasColumnName("reference_number")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.PageUrl)
            .HasColumnName("page_url")
            .HasMaxLength(1000);

        builder.Property(r => r.Browser)
            .HasColumnName("browser")
            .HasMaxLength(500);

        builder.Property(r => r.AppVersion)
            .HasColumnName("app_version")
            .HasMaxLength(50);

        builder.Property(r => r.IncludeDiagnostics)
            .HasColumnName("include_diagnostics")
            .IsRequired();

        builder.Property(r => r.DiagnosticsJson)
            .HasColumnName("diagnostics_json")
            .HasColumnType("jsonb");

        builder.Property(r => r.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(100);

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(r => r.CompanyId);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.CreatedAt);
        builder.HasIndex(r => new { r.CompanyId, r.Status });
        builder.HasIndex(r => r.ReferenceNumber).IsUnique();
    }
}
