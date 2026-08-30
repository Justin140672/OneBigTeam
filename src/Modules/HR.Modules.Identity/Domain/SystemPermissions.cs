namespace HR.Modules.Identity.Domain;

/// <summary>
/// Fixed permission identifiers for all system permissions.
/// Format: resource.action
/// </summary>
internal static class SystemPermissions
{
    // self
    public static readonly Guid SelfRead   = new("00000000-0000-0000-0001-000000000001");
    public static readonly Guid SelfEdit   = new("00000000-0000-0000-0001-000000000002");

    // employee
    public static readonly Guid EmployeeRead   = new("00000000-0000-0000-0001-000000000003");
    public static readonly Guid EmployeeEdit   = new("00000000-0000-0000-0001-000000000004");
    public static readonly Guid EmployeeCreate = new("00000000-0000-0000-0001-000000000005");
    public static readonly Guid EmployeeDelete = new("00000000-0000-0000-0001-000000000006");

    // leave
    public static readonly Guid LeaveRequest = new("00000000-0000-0000-0001-000000000007");
    public static readonly Guid LeaveApprove = new("00000000-0000-0000-0001-000000000008");

    // document
    public static readonly Guid DocumentRead   = new("00000000-0000-0000-0001-000000000009");
    public static readonly Guid DocumentManage = new("00000000-0000-0000-0001-000000000010");

    // company
    public static readonly Guid CompanyRead = new("00000000-0000-0000-0001-000000000011");
    public static readonly Guid CompanyEdit = new("00000000-0000-0000-0001-000000000012");

    // role
    public static readonly Guid RoleAssign = new("00000000-0000-0000-0001-000000000013");

    // sickness
    public static readonly Guid SicknessRead   = new("00000000-0000-0000-0001-000000000014");
    public static readonly Guid SicknessManage = new("00000000-0000-0000-0001-000000000015");

    // IAM-06: authoritative permission catalogue expansion. Every named authorization policy
    // registered in IdentityModule.AddRolePolicies (other than the raw "role:*" role-identity
    // gates and the cross-tenant "platform:admin" policy) must resolve through a permission id
    // defined here — see Authorization/PolicyCatalog.cs for the policy -> permission mapping and
    // Persistence/Configurations/RolePermissionConfiguration.cs for the role -> permission grants.

    // users (user/account administration — distinct from employee HR data)
    public static readonly Guid UsersView   = new("00000000-0000-0000-0001-000000000016");
    public static readonly Guid UsersManage = new("00000000-0000-0000-0001-000000000017");

    // hr-settings
    public static readonly Guid HrSettingsManage = new("00000000-0000-0000-0001-000000000018");

    // onboarding (Getting Started checklist)
    public static readonly Guid OnboardingView   = new("00000000-0000-0000-0001-000000000019");
    public static readonly Guid OnboardingManage = new("00000000-0000-0000-0001-000000000020");

    // subscription
    public static readonly Guid SubscriptionManage = new("00000000-0000-0000-0001-000000000021");

    // leave (broader policy administration, distinct from leave.request/leave.approve above)
    public static readonly Guid LeaveManage = new("00000000-0000-0000-0001-000000000022");

    // probation
    public static readonly Guid ProbationManage = new("00000000-0000-0000-0001-000000000023");
    public static readonly Guid ProbationReview = new("00000000-0000-0000-0001-000000000024");

    // asset
    public static readonly Guid AssetView = new("00000000-0000-0000-0001-000000000025");

    // recruitment
    public static readonly Guid RecruitmentManage = new("00000000-0000-0000-0001-000000000026");
    public static readonly Guid RecruitmentView   = new("00000000-0000-0000-0001-000000000027");
    public static readonly Guid CandidateView     = new("00000000-0000-0000-0001-000000000028");

    // shared-document (company-wide documents, e.g. policies/handbooks)
    public static readonly Guid SharedDocumentViewPublished           = new("00000000-0000-0000-0001-000000000029");
    public static readonly Guid SharedDocumentManage                  = new("00000000-0000-0000-0001-000000000030");
    public static readonly Guid SharedDocumentPublish                 = new("00000000-0000-0000-0001-000000000031");
    public static readonly Guid SharedDocumentArchive                 = new("00000000-0000-0000-0001-000000000032");
    public static readonly Guid SharedDocumentViewAcknowledgementStatus = new("00000000-0000-0000-0001-000000000033");

    // reporting
    public static readonly Guid ReportingView                  = new("00000000-0000-0000-0001-000000000034");
    public static readonly Guid ReportingViewRecruitment        = new("00000000-0000-0000-0001-000000000035");
    public static readonly Guid ReportingViewHr                 = new("00000000-0000-0000-0001-000000000036");
    public static readonly Guid ReportingViewEmployeeStarter    = new("00000000-0000-0000-0001-000000000037");
    public static readonly Guid ReportingViewLeaveSummary       = new("00000000-0000-0000-0001-000000000038");
    public static readonly Guid ReportingViewProbation          = new("00000000-0000-0000-0001-000000000039");
    public static readonly Guid ReportingViewOnboarding         = new("00000000-0000-0000-0001-000000000040");
    public static readonly Guid ReportingViewWorkloadActions    = new("00000000-0000-0000-0001-000000000041");

    // support
    public static readonly Guid SupportManage = new("00000000-0000-0000-0001-000000000042");

    // compliance (ADM-02: consolidated Compliance Centre — authoritative HR-administrator gate,
    // deliberately not granted to Company Administrator per the administrative role separation matrix)
    public static readonly Guid ComplianceView = new("00000000-0000-0000-0001-000000000043");

    // administrative alerts (ADM-03: administrative alerts & incidents inbox — compliance,
    // failed report generation, failed integrations/external-service delivery, security alerts.
    // HR Administrator only; Company Administrator deliberately excluded per the administrative
    // role separation matrix, same as compliance:view above.)
    public static readonly Guid AdministrativeAlertsView = new("00000000-0000-0000-0001-000000000044");
}
