namespace HR.Modules.Identity.Features.ResendInvite;

internal sealed record ResendInviteRequest
{
    public Guid CompanyId { get; init; }
    public Guid InviteId { get; init; }
}
