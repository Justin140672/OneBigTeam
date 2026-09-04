using HR.Modules.DataImport.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.DataImport.Persistence.Configurations;

internal sealed class ImportStagingEmployeeConfiguration : IEntityTypeConfiguration<ImportStagingEmployee>
{
    public void Configure(EntityTypeBuilder<ImportStagingEmployee> builder)
    {
        builder.ToTable("import_staging_employees");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(e => e.ImportSessionId)
            .HasColumnName("import_session_id")
            .IsRequired();

        builder.Property(e => e.RowNumber)
            .HasColumnName("row_number")
            .IsRequired();

        builder.Property(e => e.EmployeeNumber)
            .HasColumnName("employee_number")
            .HasMaxLength(100);

        builder.Property(e => e.WorkEmail)
            .HasColumnName("work_email")
            .HasMaxLength(320);

        builder.Property(e => e.ManagerReference)
            .HasColumnName("manager_reference")
            .HasMaxLength(320);

        builder.Property(e => e.DepartmentId)
            .HasColumnName("department_id");

        builder.Property(e => e.LocationId)
            .HasColumnName("location_id");

        builder.Property(e => e.EmploymentTypeId)
            .HasColumnName("employment_type_id");

        builder.Property(e => e.PositionProfileId)
            .HasColumnName("position_profile_id");

        builder.Property(e => e.ExistingEmployeeIdToUpdate)
            .HasColumnName("existing_employee_id_to_update");

        builder.Property(e => e.RawData)
            .HasColumnName("raw_data")
            .IsRequired();

        builder.Property(e => e.IsValid)
            .HasColumnName("is_valid")
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.CreatedEmployeeId)
            .HasColumnName("created_employee_id");

        builder.Property(e => e.ConfirmedAt)
            .HasColumnName("confirmed_at");

        builder.HasIndex(e => e.CompanyId);
        builder.HasIndex(e => e.ImportSessionId);
        builder.HasIndex(e => new { e.ImportSessionId, e.EmployeeNumber });
        builder.HasIndex(e => new { e.ImportSessionId, e.WorkEmail });

        builder.HasOne<ImportSession>()
            .WithMany()
            .HasForeignKey(e => e.ImportSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
