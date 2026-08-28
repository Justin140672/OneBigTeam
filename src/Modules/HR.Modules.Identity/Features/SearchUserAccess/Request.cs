namespace HR.Modules.Identity.Features.SearchUserAccess;

/// <summary>IAM-08: which override state a result must have to match, if filtering by override state.</summary>
internal enum OverrideStateFilter
{
    /// <summary>No filtering by override state.</summary>
    Any = 0,
    HasGrantOverride = 1,
    HasDenyOverride = 2,
    HasAnyOverride = 3,
    /// <summary>Has a temporary (ExpiresAt not null) override expiring within the next 14 days.</summary>
    HasExpiringOverride = 4,
}

internal sealed record SearchUserAccessRequest
{
    public Guid CompanyId { get; init; }

    /// <summary>Matches employee name or email, same convention as ListUsers' Search field.</summary>
    public string? Search { get; init; }

    /// <summary>Restrict to users who hold this role, from any source (direct, inherited or override grant).</summary>
    public Guid? RoleId { get; init; }

    /// <summary>Restrict to users who inherit a role from this position.</summary>
    public Guid? PositionId { get; init; }

    public OverrideStateFilter OverrideState { get; init; } = OverrideStateFilter.Any;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
