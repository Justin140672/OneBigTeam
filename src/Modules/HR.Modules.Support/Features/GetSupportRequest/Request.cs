namespace HR.Modules.Support.Features.GetSupportRequest;

internal sealed record GetSupportRequestRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
