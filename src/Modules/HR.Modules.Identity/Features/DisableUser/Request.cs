namespace HR.Modules.Identity.Features.DisableUser;

internal sealed record DisableUserRequest
{
    public Guid CompanyId { get; init; }
    public Guid UserId { get; init; }
}
