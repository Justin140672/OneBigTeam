namespace HR.Modules.Identity.Features.ListInvitableEmployees;

internal sealed record InvitableEmployeeItem(
    Guid EmployeeId,
    string Name,
    string? WorkEmail,
    Guid? PositionProfileId,
    string? PositionTitle);

internal sealed record ListInvitableEmployeesResponse(IReadOnlyList<InvitableEmployeeItem> Items);
