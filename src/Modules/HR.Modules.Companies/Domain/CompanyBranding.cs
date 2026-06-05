namespace HR.Modules.Companies.Domain;

internal sealed class CompanyBranding
{
    private CompanyBranding() { }

    public Guid CompanyId { get; private set; }
    public string? PrimaryLogoUrl { get; private set; }
    public string? SmallLogoUrl { get; private set; }
    public string? EmailLogoUrl { get; private set; }
    public string PrimaryColor { get; private set; } = "#000000";
    public string SecondaryColor { get; private set; } = "#6B7280";
    public string AccentColor { get; private set; } = "#3B82F6";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CompanyBranding CreateDefault(Guid companyId, DateTimeOffset now)
    {
        return new CompanyBranding
        {
            CompanyId = companyId,
            PrimaryColor = "#000000",
            SecondaryColor = "#6B7280",
            AccentColor = "#3B82F6",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void SetColors(string primaryColor, string secondaryColor, string accentColor, DateTimeOffset now)
    {
        PrimaryColor = primaryColor;
        SecondaryColor = secondaryColor;
        AccentColor = accentColor;
        UpdatedAt = now;
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
