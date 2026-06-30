using HR.Modules.Assets.Domain;

namespace HR.Modules.Assets.Features.ListAssets;

internal sealed record ListAssetsRequest
{
    public Guid CompanyId { get; init; }
    public AssetStatus? Status { get; init; }
}
