using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetEmployee;

namespace HR.Modules.Employees.Features.GetEmployeeTeamView;

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
    bool ShowLeavingTab,
    string? ProfilePhotoUrl);
