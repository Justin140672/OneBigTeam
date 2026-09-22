using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.GetMyTeamRoster;

/// <summary>
/// Full discoverable roster for the manager team-view UI ("View all team"), unlike the compact,
/// Active-only, attendance-focused HR.Modules.Employees.Features.GetMyTeam (the dashboard preview
/// widget's own data source, kept separately scoped — see that feature's own remarks). Includes
/// every status GetEmployeeTeamViewHandler still authorizes a manager to view
/// (Draft/Active/Suspended/Leaving) so every employee reachable via /team-view is also
/// discoverable here; FormerEmployee is excluded on both sides deliberately.
/// </summary>
internal sealed record GetMyTeamRosterResponse(IReadOnlyList<TeamRosterItem> Items);

internal sealed record TeamRosterItem(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string? PreferredName,
    string? JobTitle,
    string WorkEmail,
    string? ProfilePhotoUrl,
    EmploymentStatus Status);
