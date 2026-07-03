namespace HR.Modules.Companies.Domain;

internal sealed class CompanyBranding
{
    private CompanyBranding() { }

    public Guid CompanyId { get; private set; }
    public string? PrimaryLogoUrl { get; private set; }
    public string? SmallLogoUrl { get; private set; }
    public string? EmailLogoUrl { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CompanyBranding CreateDefault(Guid companyId, DateTimeOffset now)
    {
        return new CompanyBranding
        {
            CompanyId = companyId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void SetLogoUrl(BrandingAssetType assetType, string url, DateTimeOffset now)
    {
        switch (assetType)
        {
            case BrandingAssetType.PrimaryLogo:
                PrimaryLogoUrl = url;
                break;
            case BrandingAssetType.SmallLogo:
                SmallLogoUrl = url;
                break;
            case BrandingAssetType.EmailLogo:
                EmailLogoUrl = url;
                break;
        }

        UpdatedAt = now;
    }
}
