using HR.Modules.Sickness.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Sickness.Persistence.Configurations;

internal sealed class SicknessRecordConfiguration : IEntityTypeConfiguration<SicknessRecord>
{
    public void Configure(EntityTypeBuilder<SicknessRecord> builder)
    {
        builder.ToTable("sickness_records");

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

        builder.Property(r => r.CategoryId)
            .HasColumnName("category_id")
            .IsRequired();

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(r => r.StartDate)
            .HasColumnName("start_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(r => r.StartDayPart)
            .HasColumnName("start_day_part")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(r => r.EndDate)
            .HasColumnName("end_date")
            .HasColumnType("date");

        builder.Property(r => r.EndDayPart)
            .HasColumnName("end_day_part")
            .HasMaxLength(20)
            .HasConversion<string>();

        builder.Property(r => r.ReturnToWorkDate)
            .HasColumnName("return_to_work_date")
            .HasColumnType("date");

        builder.Property(r => r.EvidenceStatus)
            .HasColumnName("evidence_status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(r => r.EvidenceNotes)
            .HasColumnName("evidence_notes")
            .HasMaxLength(2000);

        builder.Property(r => r.Notes)
            .HasColumnName("notes")
            .HasMaxLength(2000);

        builder.Property(r => r.TotalDays)
            .HasColumnName("total_days")
            .HasPrecision(5, 1);

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(r => r.CompanyId);
        builder.HasIndex(r => r.EmployeeId);
        builder.HasIndex(r => r.CategoryId);
        builder.HasIndex(r => new { r.CompanyId, r.Status });
        builder.HasIndex(r => new { r.EmployeeId, r.StartDate });

        builder.HasOne<SicknessCategory>()
            .WithMany()
            .HasForeignKey(r => r.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
