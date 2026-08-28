namespace HR.Modules.Identity.Features.GetPermissionHistory;

internal sealed record GetPermissionHistoryRequest
{
    public Guid CompanyId { get; init; }

    /// <summary>Restrict to history concerning this employee/user (as target or actor).</summary>
    public Guid? EmployeeId { get; init; }

    public Guid? ActorUserId { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
