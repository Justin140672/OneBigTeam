namespace HR.Modules.Identity.Features.ListInvitableEmployees;

// ADM-01: the "select an existing employee" step of the in-admin invite workflow. Only employees
// who can actually be invited are returned — anyone who already has an account (ApplicationUser or
// Supabase-backed UserProfile) or a still-pending, non-expired invitation is excluded.
internal sealed record InvitableEmployeeItem(
    Guid EmployeeId,
    string Name,
    string? WorkEmail,
    Guid? PositionProfileId,
    string? PositionTitle);

internal sealed record ListInvitableEmployeesResponse(IReadOnlyList<InvitableEmployeeItem> Items);
