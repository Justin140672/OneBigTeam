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

            // HR Administrator: employee.read/edit/create/delete, leave.request/approve, document.manage,
            // company.read, sickness.read/manage.
            // IAM-06: leave.request added here — HrAdministrator has always held the "leave:request"
            // authorization policy (an HR Administrator can submit their own leave requests, same as
            // any employee) but the permission catalogue never reflected that grant; corrected so the
            // catalogue now matches actual endpoint behaviour instead of drifting from it.
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.EmployeeRead),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.EmployeeEdit),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.EmployeeCreate),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.EmployeeDelete),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.LeaveRequest),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.LeaveApprove),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.DocumentManage),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.CompanyRead),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SicknessRead),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SicknessManage),

            // Company Administrator: scoped to company profile/settings management only.
            // HR-facing permissions (employee, leave, document, sickness) belong to
            // HR Administrator/Manager/Employee roles, not Company Administrator.
            // IAM-06: role.assign removed — no authorization policy actually grants Company
            // Administrator role-assignment access (the "users:manage" policy that gates
            // Features/UpdateUserRoles is HR Administrator-only), so this grant was misleading
            // seeded data that implied a capability the role could never exercise.
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.CompanyRead),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.CompanyEdit),

            // IAM-06: authoritative permission catalogue expansion. Every RolePermission grant
            // below reproduces the OR-of-roles authorization behaviour that already existed in
            // IdentityModule.AddRolePolicies before this ticket, now expressed as data instead of
            // duplicated role lists — see Authorization/PolicyCatalog.cs for the policy mapping.

            // users:view / users:manage — HR Administrator territory only.
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.UsersView),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.UsersManage),

            // hr-settings:manage — HR Administrator only.
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.HrSettingsManage),

            // onboarding:view / onboarding:manage — HR or Company Administrator.
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.OnboardingView),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.OnboardingView),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.OnboardingManage),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.OnboardingManage),

            // subscription:manage — Company Administrator only. Subscription & billing is a company-
            // ownership function, not an HR one; HR Administrator's grant was removed by migration
            // RestrictSubscriptionToCompanyAdministrator (see 30-administrative-role-separation-matrix.md).
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.SubscriptionManage),

            // leave:manage — HR Administrator only.
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.LeaveManage),

            // probation:manage — HR Administrator only. probation:review — Manager or HR Administrator.
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.ProbationManage),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.ProbationReview),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.ProbationReview),

            // asset:view — Employee, Manager or HR Administrator.
            RolePermission.Create(SystemRoles.Employee, SystemPermissions.AssetView),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.AssetView),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.AssetView),

            // recruitment:manage / candidate:view — Recruiter only (deliberately not automatically
            // granted to HR Administrator — recruitment is a distinct function with its own role).
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.RecruitmentManage),
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.CandidateView),

            // recruitment:view — broad, general vacancy-board visibility.
            RolePermission.Create(SystemRoles.Employee, SystemPermissions.RecruitmentView),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.RecruitmentView),
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.RecruitmentView),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.RecruitmentView),

            // shared-document:view-published — broad company-document visibility.
            RolePermission.Create(SystemRoles.Employee, SystemPermissions.SharedDocumentViewPublished),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.SharedDocumentViewPublished),
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.SharedDocumentViewPublished),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SharedDocumentViewPublished),

            // shared-document manage/publish/archive/view-acknowledgement-status — HR Administrator only.
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SharedDocumentManage),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SharedDocumentPublish),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SharedDocumentArchive),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SharedDocumentViewAcknowledgementStatus),

            // reporting:view — Manager, Recruiter or HR Administrator (no plain Employee access).
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.ReportingView),
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.ReportingView),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.ReportingView),

            // reporting category-scoped policies — deliberately non-overlapping (see IdentityModule
            // comments): a Recruiter without HrAdministrator sees only recruitment, and vice versa.
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.ReportingViewRecruitment),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.ReportingViewHr),

            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.ReportingViewEmployeeStarter),
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.ReportingViewEmployeeStarter),

            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.ReportingViewLeaveSummary),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.ReportingViewLeaveSummary),

            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.ReportingViewProbation),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.ReportingViewProbation),

            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.ReportingViewOnboarding),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.ReportingViewOnboarding),

            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.ReportingViewWorkloadActions),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.ReportingViewWorkloadActions),

            // reporting:view-equality — Ticket 6: anonymous aggregate Equality & Diversity report.
            // HR Administrator only; deliberately not Manager/Recruiter/Company Administrator.
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.ReportingViewEquality),

            // support:manage — HR or Company Administrator (closest existing approximation; no
            // dedicated "platform staff" role exists yet — see IdentityModule comment).
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SupportManage),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.SupportManage),

            // compliance:view — ADM-02 consolidated Compliance Centre. HR Administrator only;
            // Company Administrator is deliberately excluded (administrative role separation).
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.ComplianceView)
        );
    }
}
