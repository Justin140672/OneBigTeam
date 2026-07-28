namespace HR.Modules.Employees.Features.GetMyTeam;

internal sealed record TeamMemberItem(
    Guid EmployeeId,
    string FullName,
    string? JobTitle,
    string? PhoneNumber,
    string WorkEmail,
    string? ProfilePhotoUrl,
    // "Sick" | "OnLeave" | "AtWork" — Sick takes priority if somehow both apply on the same day.
    string Status);

internal sealed record GetMyTeamResponse(IReadOnlyList<TeamMemberItem> Items);
