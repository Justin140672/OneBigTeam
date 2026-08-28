namespace HR.Modules.Identity.Features.GetAccessReview;

/// <summary>One privilege a user holds and where it comes from.</summary>
internal sealed record PrivilegeSourceItem(
    Guid RoleId,
    string RoleName,
    string Source, // "Direct" | "Position:{PositionName}" | "Override"
    DateTimeOffset? OverrideExpiresAt,
    bool IsExpiringSoon);

internal sealed record AccessReviewItem(
    Guid EmployeeId,
    Guid? UserId,
    string Name,
    string Email,
    IReadOnlyList<PrivilegeSourceItem> Privileges);

internal sealed record GetAccessReviewResponse(IReadOnlyList<AccessReviewItem> Items, int TotalCount);
