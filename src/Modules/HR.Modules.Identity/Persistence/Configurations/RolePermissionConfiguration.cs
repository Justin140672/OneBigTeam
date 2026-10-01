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
            RolePermission.Create(SystemRoles.Employee, SystemPermissions.SelfRead),
            RolePermission.Create(SystemRoles.Employee, SystemPermissions.SelfEdit),
            RolePermission.Create(SystemRoles.Employee, SystemPermissions.LeaveRequest),
            RolePermission.Create(SystemRoles.Employee, SystemPermissions.DocumentRead),

            RolePermission.Create(SystemRoles.Manager, SystemPermissions.SelfRead),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.SelfEdit),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.EmployeeRead),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.LeaveRequest),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.LeaveApprove),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.DocumentRead),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.SicknessRead),

            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.EmployeeRead),
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.EmployeeCreate),
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.DocumentRead),

            // HR Administrator: employee.read/edit/create/delete, leave.request/approve, support.request/manage,
            // document.manage, company.read, sickness.read/manage.
            // IAM-06: leave.request added here — HrAdministrator has always held the "leave:request"
            // authorization policy (an HR Administrator can submit their own leave requests, same as
            // any employee) but the permission catalogue never reflected that grant; corrected so the
            // catalogue now matches actual endpoint behaviour instead of drifting from it.
            // Ticket 6: support.request added here — HR Administrator can submit their own support requests
            // (self-service) as well as manage the support queue (support.manage).
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.EmployeeRead),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.EmployeeEdit),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.EmployeeCreate),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.EmployeeDelete),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.LeaveRequest),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.LeaveApprove),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SupportRequest),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.DocumentManage),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.CompanyRead),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SicknessRead),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SicknessManage),

            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.CompanyRead),
            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.CompanyEdit),


            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.UsersView),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.UsersManage),

            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.HrSettingsManage),

            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.OnboardingView),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.OnboardingManage),

            RolePermission.Create(SystemRoles.CompanyAdministrator, SystemPermissions.SubscriptionManage),

            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.LeaveManage),

            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.ProbationManage),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.ProbationReview),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.ProbationReview),

            RolePermission.Create(SystemRoles.Employee, SystemPermissions.AssetView),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.AssetView),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.AssetView),

            // recruitment:manage — Recruiter only (deliberately not automatically granted to
            // HR Administrator — recruitment is a distinct function with its own role). Ticket 7:
            // candidate.view permission removed as redundant; recruitment:manage is the sole
            // authoritative permission for all recruiter candidate operations.
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.RecruitmentManage),

            RolePermission.Create(SystemRoles.Employee, SystemPermissions.RecruitmentView),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.RecruitmentView),
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.RecruitmentView),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.RecruitmentView),

            RolePermission.Create(SystemRoles.Employee, SystemPermissions.SharedDocumentViewPublished),
            RolePermission.Create(SystemRoles.Manager, SystemPermissions.SharedDocumentViewPublished),
            RolePermission.Create(SystemRoles.Recruiter, SystemPermissions.SharedDocumentViewPublished),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SharedDocumentViewPublished),

            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SharedDocumentManage),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SharedDocumentPublish),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SharedDocumentArchive),
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SharedDocumentViewAcknowledgementStatus),

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

            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.SupportManage),

            // compliance:view — ADM-02 consolidated Compliance Centre. HR Administrator only;
            // Company Administrator is deliberately excluded (administrative role separation).
            RolePermission.Create(SystemRoles.HrAdministrator, SystemPermissions.ComplianceView)
        );
    }
}
