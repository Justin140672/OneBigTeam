namespace HR.Modules.Employees.Features.GetMyTeam;

internal sealed record TeamMemberItem(
    Guid EmployeeId,
    string FullName,
    string? JobTitle,
    string? PhoneNumber,
    string WorkEmail,
    string? ProfilePhotoUrl);

internal sealed record GetMyTeamResponse(IReadOnlyList<TeamMemberItem> Items);
