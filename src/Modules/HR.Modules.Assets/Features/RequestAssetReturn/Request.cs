namespace HR.Modules.Assets.Features.RequestAssetReturn;

internal sealed record RequestAssetReturnRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public Guid RequestedBy { get; init; }
}
