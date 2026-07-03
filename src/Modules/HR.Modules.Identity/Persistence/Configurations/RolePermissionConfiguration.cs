using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");

        builder.HasKey(rp => new { rp.RoleId, rp.PermissionId });

        builder.Property(rp => rp.RoleId)
            .HasColumnName("role_id");

        builder.Property(rp => rp.PermissionId)
            .HasColumnName("permission_id");

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Permission>()
            .WithMany()
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasData(
            // Employee: self.read, self.edit, leave.request, document.read
            RolePermission.Create(SystemRoles.Employee, SystemPermissions.SelfRead),
            RolePermission.Create(SystemRoles.Employee, SystemPermissions.SelfEdit),
            RolePermission.Create(SystemRoles.Employee, SystemPermissions.LeaveRequest),
            RolePermission.Create(SystemRoles.Employee, SystemPermissions.DocumentRead),

            // Manager: self.read, self.edit, employee.read, leave.request, leave.approve, document.read, sickness.read
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.SelfRead),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.SelfEdit),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.EmployeeRead),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.LeaveRequest),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.LeaveApprove),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.DocumentRead),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.SicknessRead),

            // Recruiter: employee.read, employee.create, document.read
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.EmployeeRead),
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.EmployeeCreate),
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.DocumentRead),

            // HR Administrator: employee.read/edit/create/delete, leave.approve, document.manage, company.read, sickness.read/manage
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.EmployeeRead),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.EmployeeEdit),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.EmployeeCreate),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.EmployeeDelete),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.LeaveApprove),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.DocumentManage),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.CompanyRead),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SicknessRead),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SicknessManage),

            // Finance: self.read, employee.read, company.read
            RolePermission.Create(SystemRoles.Finance, SystemPermissions.SelfRead),
            RolePermission.Create(SystemRoles.Finance, SystemPermissions.EmployeeRead),
            RolePermission.Create(SystemRoles.Finance, SystemPermissions.CompanyRead),

            // Company Administrator: all permissions
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.SelfRead),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.SelfEdit),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.EmployeeRead),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.EmployeeEdit),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.EmployeeCreate),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.EmployeeDelete),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.LeaveRequest),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.LeaveApprove),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.DocumentManage),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.CompanyRead),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.CompanyEdit),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.RoleAssign),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.SicknessRead),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.SicknessManage)
        );
    }
}
