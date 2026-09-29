using HR.Modules.Employees.Contracts;
namespace HR.Modules.Identity.Features.ListUsers;

internal sealed record UserAdministrationListItem(
    Guid EmployeeId,
    Guid? UserId,
    string Name,
    string Email,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> RoleNames,
    string AccountStatus,
    string InvitationStatus,
    Guid? InviteId,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    Guid? PositionProfileId = null,
    string? PositionTitle = null);

internal sealed record ListUsersResponse(
    IReadOnlyList<UserAdministrationListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);
