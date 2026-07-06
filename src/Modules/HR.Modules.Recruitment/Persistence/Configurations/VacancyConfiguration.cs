using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class VacancyConfiguration : IEntityTypeConfiguration<Vacancy>
{
    public void Configure(EntityTypeBuilder<Vacancy> builder)
    {
        builder.ToTable("vacancies");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(v => v.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(v => v.DepartmentId)
            .HasColumnName("department_id");

        builder.Property(v => v.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(v => v.Description)
            .HasColumnName("description")
            .HasMaxLength(4000);

        builder.Property(v => v.Location)
            .HasColumnName("location")
            .HasMaxLength(200);

        builder.Property(v => v.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(v => v.HiringManagerId)
            .HasColumnName("hiring_manager_id")
            .IsRequired();

        builder.Property(v => v.OpenedAt)
            .HasColumnName("opened_at");

        builder.Property(v => v.ClosedAt)
            .HasColumnName("closed_at");

        builder.Property(v => v.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(v => v.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(v => v.CompanyId);
        builder.HasIndex(v => v.DepartmentId);
        builder.HasIndex(v => new { v.CompanyId, v.Status });
    }
}
