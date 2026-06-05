using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.UploadCompanyLogo;

internal sealed record UploadCompanyLogoRequest
{
    public Guid Id { get; init; }
    public BrandingAssetType AssetType { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
}
