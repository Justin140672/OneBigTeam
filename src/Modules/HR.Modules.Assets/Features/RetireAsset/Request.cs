namespace HR.Modules.Assets.Features.RetireAsset;

internal sealed record RetireAssetRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
