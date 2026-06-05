using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Storage;

internal sealed class StubBrandingStorage : IBrandingStorage
{
    public Task<string> StoreLogoAsync(
        Guid companyId,
        BrandingAssetType assetType,
        string fileName,
        string contentType,
        long fileSizeBytes,
        CancellationToken cancellationToken)
    {
        var assetSegment = assetType switch
        {
            BrandingAssetType.PrimaryLogo => "primary-logo",
            BrandingAssetType.SmallLogo => "small-logo",
            BrandingAssetType.EmailLogo => "email-logo",
            _ => "logo",
        };

        var url = $"/branding/{companyId}/{assetSegment}/{fileName}";
        return Task.FromResult(url);
    }
}
