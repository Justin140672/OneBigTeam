using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetEmployee;

namespace HR.Modules.Employees.Features.GetEmployeeTeamView;

/// <summary>
/// The manager-facing, operational-only view of a direct or indirect report's employee record.
/// See 26-permissions-access-ux.md's field-level access matrix for the approved field list and
/// GetEmployeeTeamViewHandler's database projection, which never selects a column not represented
/// here — this is not the full GetEmployeeResponse with fields nulled out.
/// </summary>
internal sealed record GetEmployeeTeamViewResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LocationId,
    string? LocationName,
    Guid? PositionProfileId,
    string? PositionTitle,
    Guid? ManagerId,
    string? ManagerFullName,
    int DirectReportsCount,
    IReadOnlyList<ReportingChainItem> ReportingChain,
    string FirstName,
    string LastName,
    string? PreferredName,
    string WorkEmail,
    DateOnly StartDate,
    EmploymentStatus Status,
    string? EmployeeNumber,
    Guid? EmploymentTypeId,
    string? EmploymentTypeName,
    bool ShowOnboardingTab,
    bool ShowProbationTab,
    bool ShowOffboardingTab,
    bool ShowLeavingTab);
