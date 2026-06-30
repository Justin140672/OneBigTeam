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

        builder.Property(e => e.HasSystemAccess)
            .HasColumnName("has_system_access")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.WorkingDaysOverride)
            .HasColumnName("working_days_override");

        builder.Property(e => e.HoursPerDayOverride)
            .HasColumnName("hours_per_day_override")
            .HasPrecision(4, 2);

        builder.Property(e => e.ProfileImageUrl)
            .HasColumnName("profile_image_url")
            .HasMaxLength(2048);

        builder.Property(e => e.PreferredName)
            .HasColumnName("preferred_name")
            .HasMaxLength(100);

        builder.Property(e => e.DateOfBirth)
            .HasColumnName("date_of_birth");

        builder.Property(e => e.Nationality)
            .HasColumnName("nationality")
            .HasMaxLength(100);

        builder.Property(e => e.Gender)
            .HasColumnName("gender")
            .HasMaxLength(50);

        builder.Property(e => e.GenderOther)
            .HasColumnName("gender_other")
            .HasMaxLength(200);

        builder.Property(e => e.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(30);

        builder.Property(e => e.HomePhone)
            .HasColumnName("home_phone")
            .HasMaxLength(30);

        builder.Property(e => e.AddressLine1)
            .HasColumnName("address_line1")
            .HasMaxLength(200);

        builder.Property(e => e.AddressLine2)
            .HasColumnName("address_line2")
            .HasMaxLength(200);

        builder.Property(e => e.City)
            .HasColumnName("city")
            .HasMaxLength(100);

        builder.Property(e => e.County)
            .HasColumnName("county")
            .HasMaxLength(100);

        builder.Property(e => e.PostCode)
            .HasColumnName("post_code")
            .HasMaxLength(20);

        builder.Property(e => e.Country)
            .HasColumnName("country")
            .HasMaxLength(100);

        builder.Property(e => e.EmployeeNumber)
            .HasColumnName("employee_number")
            .HasMaxLength(50);

        builder.Property(e => e.EmploymentTypeId)
            .HasColumnName("employment_type_id");

        builder.HasOne<EmploymentType>()
            .WithMany()
            .HasForeignKey(e => e.EmploymentTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(e => e.ContinuousServiceDate)
            .HasColumnName("continuous_service_date");

        builder.Property(e => e.ProbationEndDate)
            .HasColumnName("probation_end_date");

        builder.Property(e => e.LeavingDate)
            .HasColumnName("leaving_date");

        builder.Property(e => e.Notes)
            .HasColumnName("notes")
            .HasMaxLength(4000);

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
