using HR.Modules.Probation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Probation.Persistence.Configurations;

internal sealed class ProbationRecordConfiguration : IEntityTypeConfiguration<ProbationRecord>
{
    public void Configure(EntityTypeBuilder<ProbationRecord> builder)
    {
        builder.ToTable("probation_records");

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

        builder.Property(r => r.ManagerEmployeeId)
            .HasColumnName("manager_employee_id")
            .IsRequired();

        builder.Property(r => r.StartDate)
            .HasColumnName("start_date")
            .IsRequired();

        builder.Property(r => r.ExpectedEndDate)
            .HasColumnName("expected_end_date")
            .IsRequired();

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.Notes)
            .HasColumnName("notes")
            .HasMaxLength(2000);

        builder.Property(r => r.ExtensionReason)
            .HasColumnName("extension_reason")
            .HasMaxLength(1000);

        builder.Property(r => r.DecisionDate)
            .HasColumnName("decision_date");

        builder.Property(r => r.DecisionMakerEmployeeId)
            .HasColumnName("decision_maker_employee_id");

        builder.Property(r => r.OutcomeNotes)
            .HasColumnName("outcome_notes")
            .HasMaxLength(2000);

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(r => r.CompanyId);
        builder.HasIndex(r => new { r.CompanyId, r.EmployeeId });
        builder.HasIndex(r => new { r.CompanyId, r.Status });
        builder.HasIndex(r => new { r.CompanyId, r.ManagerEmployeeId });
    }
}
