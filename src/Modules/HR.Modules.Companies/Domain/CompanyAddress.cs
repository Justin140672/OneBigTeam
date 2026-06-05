namespace HR.Modules.Companies.Domain;

internal sealed class CompanyAddress
{
    private CompanyAddress() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public CompanyAddressType Type { get; private set; }
    public string Line1 { get; private set; } = string.Empty;
    public string? Line2 { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string? Region { get; private set; }
    public string? PostalCode { get; private set; }
    public string CountryCode { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CompanyAddress Create(
        Guid id,
        Guid companyId,
        CompanyAddressType type,
        string line1,
        string? line2,
        string city,
        string? region,
        string? postalCode,
        string countryCode,
        DateTimeOffset now)
    {
        return new CompanyAddress
        {
            Id = id,
            CompanyId = companyId,
            Type = type,
            Line1 = line1,
            Line2 = line2,
            City = city,
            Region = region,
            PostalCode = postalCode,
            CountryCode = countryCode,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        string line1,
        string? line2,
        string city,
        string? region,
        string? postalCode,
        string countryCode,
        DateTimeOffset now)
    {
        Line1 = line1;
        Line2 = line2;
        City = city;
        Region = region;
        PostalCode = postalCode;
        CountryCode = countryCode;
        UpdatedAt = now;
    }
}
