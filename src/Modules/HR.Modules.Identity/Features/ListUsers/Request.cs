namespace HR.Modules.Identity.Features.ListUsers;

internal sealed record ListUsersRequest
{
    public Guid CompanyId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public string? Search { get; init; }
}
