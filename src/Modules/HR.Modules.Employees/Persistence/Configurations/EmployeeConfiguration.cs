using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Employees.Persistence.Configurations;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(e => e.DepartmentId)
            .HasColumnName("department_id");

        builder.Property(e => e.PositionProfileId)
            .HasColumnName("position_profile_id");

        builder.Property(e => e.ManagerId)
            .HasColumnName("manager_id");

        builder.Property(e => e.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.WorkEmail)
            .HasColumnName("work_email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(e => e.PersonalEmail)
            .HasColumnName("personal_email")
            .HasMaxLength(320);

        builder.HasIndex(e => new { e.CompanyId, e.WorkEmail })
            .IsUnique();

        builder.Property(e => e.StartDate)
            .HasColumnName("start_date")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(e => e.CompanyId);
        builder.HasIndex(e => e.DepartmentId);
        builder.HasIndex(e => e.PositionProfileId);
        builder.HasIndex(e => e.ManagerId);
        builder.HasIndex(e => new { e.CompanyId, e.Status });
    }
}
