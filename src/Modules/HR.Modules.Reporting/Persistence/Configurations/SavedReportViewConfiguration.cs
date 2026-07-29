using HR.Modules.Reporting.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Reporting.Persistence.Configurations;

internal sealed class SavedReportViewConfiguration : IEntityTypeConfiguration<SavedReportView>
{
    public void Configure(EntityTypeBuilder<SavedReportView> builder)
    {
        builder.ToTable("saved_report_views");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(v => v.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(v => v.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(v => v.ReportId)
            .HasColumnName("report_id")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(v => v.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(v => v.FilterCriteriaJson)
            .HasColumnName("filter_criteria_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(v => v.IsDefault)
            .HasColumnName("is_default")
            .IsRequired();

        builder.Property(v => v.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(v => new { v.CompanyId, v.UserId, v.ReportId })
            .HasDatabaseName("ix_saved_report_views_company_id_user_id_report_id");
    }
}
