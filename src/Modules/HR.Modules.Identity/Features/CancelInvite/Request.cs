namespace HR.Modules.Identity.Features.CancelInvite;

internal sealed record CancelInviteRequest
{
    public Guid CompanyId { get; init; }
    public Guid InviteId { get; init; }
}
