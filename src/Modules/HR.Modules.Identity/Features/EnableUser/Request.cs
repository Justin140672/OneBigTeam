namespace HR.Modules.Identity.Features.EnableUser;

internal sealed record EnableUserRequest
{
    public Guid CompanyId { get; init; }
    public Guid UserId { get; init; }
}
