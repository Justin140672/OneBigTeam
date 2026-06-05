namespace HR.Modules.Companies.Domain;

internal sealed class CompanyBranding
{
    private CompanyBranding() { }

    public Guid CompanyId { get; private set; }
    public string? PrimaryLogoUrl { get; private set; }
    public string? SmallLogoUrl { get; private set; }
    public string? EmailLogoUrl { get; private set; }
    public string PrimaryColor { get; private set; } = string.Empty;
    public string SecondaryColor { get; private set; } = string.Empty;
    public string AccentColor { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CompanyBranding CreateDefault(Guid companyId, DateTimeOffset now)
    {
        return new CompanyBranding
        {
            CompanyId = companyId,
            PrimaryLogoUrl = null,
            SmallLogoUrl = null,
            EmailLogoUrl = null,
            PrimaryColor = "#0055AA",
            SecondaryColor = "#1F2937",
            AccentColor = "#0EA5E9",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}