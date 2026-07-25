using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Employees.Persistence.Configurations;

internal sealed class CompensationConfiguration : IEntityTypeConfiguration<Compensation>
{
    public void Configure(EntityTypeBuilder<Compensation> builder)
    {
        builder.ToTable("compensations");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(c => c.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(c => c.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(c => c.EffectiveFrom)
            .HasColumnName("effective_from")
            .IsRequired();

        builder.Property(c => c.EffectiveTo)
            .HasColumnName("effective_to");

        builder.Property(c => c.SalaryType)
            .HasColumnName("salary_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.Salary)
            .HasColumnName("salary")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(c => c.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(c => c.HoursPerWeek)
            .HasColumnName("hours_per_week")
            .HasPrecision(5, 2);

        builder.Property(c => c.FTE)
            .HasColumnName("fte")
            .HasPrecision(4, 2);

        builder.Property(c => c.Notes)
            .HasColumnName("notes")
            .HasMaxLength(4000);

        builder.Property(c => c.Reason)
            .HasColumnName("reason")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(c => c.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(c => c.CompanyId);
        builder.HasIndex(c => new { c.CompanyId, c.EmployeeId });
    }
}
