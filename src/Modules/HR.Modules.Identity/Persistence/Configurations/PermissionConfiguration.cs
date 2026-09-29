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
            Permission.Create(SystemPermissions.SelfRead,        "self.read",        seedDate),
            Permission.Create(SystemPermissions.SelfEdit,        "self.edit",        seedDate),

            Permission.Create(SystemPermissions.EmployeeRead,    "employee.read",    seedDate),
            Permission.Create(SystemPermissions.EmployeeEdit,    "employee.edit",    seedDate),
            Permission.Create(SystemPermissions.EmployeeCreate,  "employee.create",  seedDate),
            Permission.Create(SystemPermissions.EmployeeDelete,  "employee.delete",  seedDate),

            Permission.Create(SystemPermissions.LeaveRequest,    "leave.request",    seedDate),
            Permission.Create(SystemPermissions.LeaveApprove,    "leave.approve",    seedDate),

            Permission.Create(SystemPermissions.DocumentRead,    "document.read",    seedDate),
            Permission.Create(SystemPermissions.DocumentManage,  "document.manage",  seedDate),

            Permission.Create(SystemPermissions.CompanyRead,     "company.read",     seedDate),
            Permission.Create(SystemPermissions.CompanyEdit,     "company.edit",     seedDate),

            Permission.Create(SystemPermissions.RoleAssign,      "role.assign",      seedDate),

            Permission.Create(SystemPermissions.SicknessRead,    "sickness.read",    seedDate),
            Permission.Create(SystemPermissions.SicknessManage,  "sickness.manage",  seedDate),

            Permission.Create(SystemPermissions.UsersView,   "users.view",   seedDate),
            Permission.Create(SystemPermissions.UsersManage, "users.manage", seedDate),

            Permission.Create(SystemPermissions.HrSettingsManage, "hr-settings.manage", seedDate),

            Permission.Create(SystemPermissions.OnboardingView,   "onboarding.view",   seedDate),
            Permission.Create(SystemPermissions.OnboardingManage, "onboarding.manage", seedDate),

            Permission.Create(SystemPermissions.SubscriptionManage, "subscription.manage", seedDate),

            Permission.Create(SystemPermissions.LeaveManage, "leave.manage", seedDate),

            Permission.Create(SystemPermissions.ProbationManage, "probation.manage", seedDate),
            Permission.Create(SystemPermissions.ProbationReview, "probation.review", seedDate),

            Permission.Create(SystemPermissions.AssetView, "asset.view", seedDate),

            Permission.Create(SystemPermissions.RecruitmentManage, "recruitment.manage", seedDate),
            Permission.Create(SystemPermissions.RecruitmentView,   "recruitment.view",   seedDate),

            Permission.Create(SystemPermissions.SharedDocumentViewPublished, "shared-document.view-published", seedDate),
            Permission.Create(SystemPermissions.SharedDocumentManage, "shared-document.manage", seedDate),
            Permission.Create(SystemPermissions.SharedDocumentPublish, "shared-document.publish", seedDate),
            Permission.Create(SystemPermissions.SharedDocumentArchive, "shared-document.archive", seedDate),
            Permission.Create(SystemPermissions.SharedDocumentViewAcknowledgementStatus, "shared-document.view-acknowledgement-status", seedDate),

            Permission.Create(SystemPermissions.ReportingView,               "reporting.view",                  seedDate),
            Permission.Create(SystemPermissions.ReportingViewRecruitment,    "reporting.view-recruitment",      seedDate),
            Permission.Create(SystemPermissions.ReportingViewHr,             "reporting.view-hr",               seedDate),
            Permission.Create(SystemPermissions.ReportingViewEmployeeStarter,"reporting.view-employee-starter", seedDate),
            Permission.Create(SystemPermissions.ReportingViewLeaveSummary,   "reporting.view-leave-summary",    seedDate),
            Permission.Create(SystemPermissions.ReportingViewProbation,      "reporting.view-probation",        seedDate),
            Permission.Create(SystemPermissions.ReportingViewOnboarding,     "reporting.view-onboarding",       seedDate),
            Permission.Create(SystemPermissions.ReportingViewWorkloadActions,"reporting.view-workload-actions", seedDate),
            Permission.Create(SystemPermissions.ReportingViewEquality,       "reporting.view-equality",         seedDate),

            Permission.Create(SystemPermissions.SupportManage, "support.manage", seedDate),
            Permission.Create(SystemPermissions.SupportRequest, "support.request", seedDate),

            Permission.Create(SystemPermissions.ComplianceView, "compliance.view", seedDate)
        );
    }
}
