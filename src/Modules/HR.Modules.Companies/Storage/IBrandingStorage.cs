using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Storage;

internal interface IBrandingStorage
{
    Task<string> StoreLogoAsync(
        Guid companyId,
        BrandingAssetType assetType,
        string fileName,
        string contentType,
        long fileSizeBytes,
        CancellationToken cancellationToken);
}
