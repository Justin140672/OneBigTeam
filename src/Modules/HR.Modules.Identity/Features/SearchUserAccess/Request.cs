namespace HR.Modules.Identity.Features.SearchUserAccess;

internal enum OverrideStateFilter
{
    Any = 0,
    HasGrantOverride = 1,
    HasDenyOverride = 2,
    HasAnyOverride = 3,
    HasExpiringOverride = 4,
}

internal sealed record SearchUserAccessRequest
{
    public Guid CompanyId { get; init; }

    public string? Search { get; init; }

    public Guid? RoleId { get; init; }

    public Guid? PositionId { get; init; }

    public OverrideStateFilter OverrideState { get; init; } = OverrideStateFilter.Any;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
