namespace HR.Modules.Identity.Features.GetAccessReview;

internal sealed record PrivilegeSourceItem(
    Guid RoleId,
    string RoleName,
    string Source,
    DateTimeOffset? OverrideExpiresAt,
    bool IsExpiringSoon);

internal sealed record AccessReviewItem(
    Guid EmployeeId,
    Guid? UserId,
    string Name,
    string Email,
    IReadOnlyList<PrivilegeSourceItem> Privileges);

internal sealed record GetAccessReviewResponse(IReadOnlyList<AccessReviewItem> Items, int TotalCount);
