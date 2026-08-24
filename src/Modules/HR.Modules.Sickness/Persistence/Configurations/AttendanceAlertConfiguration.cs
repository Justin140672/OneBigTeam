using HR.Modules.Sickness.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Sickness.Persistence.Configurations;

internal sealed class AttendanceAlertConfiguration : IEntityTypeConfiguration<AttendanceAlert>
{
    public void Configure(EntityTypeBuilder<AttendanceAlert> builder)
    {
        builder.ToTable("attendance_alerts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(a => a.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(a => a.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(a => a.Rule)
            .HasColumnName("rule")
            .HasMaxLength(40)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(a => a.EvidencePeriodStart)
            .HasColumnName("evidence_period_start")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(a => a.EvidencePeriodEnd)
            .HasColumnName("evidence_period_end")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(a => a.OccurrenceCount)
            .HasColumnName("occurrence_count")
            .IsRequired();

        builder.Property(a => a.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(a => a.CompanyId);

        // SICK-04 duplicate-prevention guard: never more than one alert per employee+rule+evidence
        // window. Enforced at the database level (not just in application code) so a job retry
        // racing another instance still cannot create a duplicate.
        builder.HasIndex(a => new { a.CompanyId, a.EmployeeId, a.Rule, a.EvidencePeriodStart, a.EvidencePeriodEnd })
            .IsUnique();
    }
}
