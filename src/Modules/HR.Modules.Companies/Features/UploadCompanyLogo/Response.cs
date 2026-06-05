using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.UploadCompanyLogo;

internal sealed record UploadCompanyLogoResponse(
    Guid CompanyId,
    BrandingAssetType AssetType,
    string LogoUrl,
    DateTimeOffset UpdatedAt);
