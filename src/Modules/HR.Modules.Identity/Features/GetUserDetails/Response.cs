namespace HR.Modules.Identity.Features.GetUserDetails;

internal sealed record GetUserDetailsResponse(
    Guid EmployeeId,
    Guid? UserId,
    string Name,
    string Email,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> RoleNames,
    string AccountStatus,
    string InvitationStatus,
    Guid? InviteId,
    DateTimeOffset? InviteExpiresAt,
    string? CreatedByName,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    // The linked employee's current position (ADM-01).
    Guid? PositionProfileId = null,
    string? PositionTitle = null);
