using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Authorization;

/// <summary>
/// IAM-06: the single authoritative map from a named FastEndpoints/ASP.NET Core authorization
/// policy string (as passed to Endpoint.Policies("...")) to the permission id that policy
/// resolves. IdentityModule.AddRolePolicies registers every entry here as a PermissionRequirement
/// policy, and HR.Modules.Identity.Tests' policy-matrix test enumerates this same catalogue
/// against every SystemRoles value — so a role-mapping change (RolePermissionConfiguration) and a
/// policy definition (this file) can never silently drift apart from what the UI's
/// GetEffectiveAccess view reports, because both read from the same RolePermission data.
///
/// Deliberately excluded from this catalogue (kept as raw role-identity checks in
/// IdentityModule.AddRolePolicies, unchanged by IAM-06):
///   - "platform:admin" — cross-tenant platform-staff gate with no company/permission concept.
///   - "role:employee" / "role:manager" / "role:recruiter" / "role:hr-administrator" /
///     "role:company-administrator" — these test role identity itself (e.g. "does this user carry
///     the Manager role"), not a granted capability, so a permission indirection would add nothing.
/// </summary>
internal static class PolicyCatalog
{
    public static readonly IReadOnlyDictionary<string, Guid> PermissionPolicies = new Dictionary<string, Guid>
    {
        ["employee:manage"] = SystemPermissions.EmployeeEdit,
        ["company:manage"] = SystemPermissions.CompanyEdit,
        ["support:manage"] = SystemPermissions.SupportManage,
        ["hr-settings:manage"] = SystemPermissions.HrSettingsManage,
        ["users:view"] = SystemPermissions.UsersView,
        ["users:manage"] = SystemPermissions.UsersManage,
        ["onboarding:view"] = SystemPermissions.OnboardingView,
        ["onboarding:manage"] = SystemPermissions.OnboardingManage,
        ["subscription:manage"] = SystemPermissions.SubscriptionManage,
        ["leave:request"] = SystemPermissions.LeaveRequest,
        ["leave:approve"] = SystemPermissions.LeaveApprove,
        ["leave:manage"] = SystemPermissions.LeaveManage,
        ["probation:manage"] = SystemPermissions.ProbationManage,
        ["probation:review"] = SystemPermissions.ProbationReview,
        ["sickness:review"] = SystemPermissions.SicknessRead,
        ["sickness:manage"] = SystemPermissions.SicknessManage,
        ["sickness:view-team"] = SystemPermissions.SicknessRead,
        ["asset:view"] = SystemPermissions.AssetView,
        ["recruitment:manage"] = SystemPermissions.RecruitmentManage,
        ["recruitment:view"] = SystemPermissions.RecruitmentView,
        ["candidate:view"] = SystemPermissions.CandidateView,
        ["shared-document:view-published"] = SystemPermissions.SharedDocumentViewPublished,
        ["shared-document:manage"] = SystemPermissions.SharedDocumentManage,
        ["shared-document:publish"] = SystemPermissions.SharedDocumentPublish,
        ["shared-document:archive"] = SystemPermissions.SharedDocumentArchive,
        ["shared-document:view-acknowledgement-status"] = SystemPermissions.SharedDocumentViewAcknowledgementStatus,
        ["reporting:view"] = SystemPermissions.ReportingView,
        ["reporting:view-recruitment"] = SystemPermissions.ReportingViewRecruitment,
        ["reporting:view-hr"] = SystemPermissions.ReportingViewHr,
        ["reporting:view-employee-starter"] = SystemPermissions.ReportingViewEmployeeStarter,
        ["reporting:view-leave-summary"] = SystemPermissions.ReportingViewLeaveSummary,
        ["reporting:view-probation"] = SystemPermissions.ReportingViewProbation,
        ["reporting:view-onboarding"] = SystemPermissions.ReportingViewOnboarding,
        ["reporting:view-workload-actions"] = SystemPermissions.ReportingViewWorkloadActions,
    };
}
