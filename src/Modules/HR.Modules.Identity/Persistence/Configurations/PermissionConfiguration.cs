using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(p => p.Name)
            .IsUnique();

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        var seedDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        builder.HasData(
            // self
            Permission.Create(SystemPermissions.SelfRead,        "self.read",        seedDate),
            Permission.Create(SystemPermissions.SelfEdit,        "self.edit",        seedDate),

            // employee
            Permission.Create(SystemPermissions.EmployeeRead,    "employee.read",    seedDate),
            Permission.Create(SystemPermissions.EmployeeEdit,    "employee.edit",    seedDate),
            Permission.Create(SystemPermissions.EmployeeCreate,  "employee.create",  seedDate),
            Permission.Create(SystemPermissions.EmployeeDelete,  "employee.delete",  seedDate),

            // leave
            Permission.Create(SystemPermissions.LeaveRequest,    "leave.request",    seedDate),
            Permission.Create(SystemPermissions.LeaveApprove,    "leave.approve",    seedDate),

            // document
            Permission.Create(SystemPermissions.DocumentRead,    "document.read",    seedDate),
            Permission.Create(SystemPermissions.DocumentManage,  "document.manage",  seedDate),

            // company
            Permission.Create(SystemPermissions.CompanyRead,     "company.read",     seedDate),
            Permission.Create(SystemPermissions.CompanyEdit,     "company.edit",     seedDate),

            // role
            Permission.Create(SystemPermissions.RoleAssign,      "role.assign",      seedDate),

            // sickness
            Permission.Create(SystemPermissions.SicknessRead,    "sickness.read",    seedDate),
            Permission.Create(SystemPermissions.SicknessManage,  "sickness.manage",  seedDate)
        );
    }
}
