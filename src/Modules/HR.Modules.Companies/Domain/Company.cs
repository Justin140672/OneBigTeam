namespace HR.Modules.Companies.Domain;

internal sealed class Company
{
    private readonly List<CompanyAddress> _addresses = [];

    private Company() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public CompanyStatus Status { get; private set; }

    // Kept as a computed shim so existing read-sites (UI badges, response DTOs) that only care
    // about "is this company usable right now" don't need a sweep — grepped all readers of
    // Company.IsActive before making this change and none of them need to distinguish
    // PendingVerification from Deactivated, they only ever branched on true/false.
    public bool IsActive => Status == CompanyStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public CompanySettings? Settings { get; private set; }
    public CompanyBranding? Branding { get; private set; }
    public IReadOnlyCollection<CompanyAddress> Addresses => _addresses;

    public static Company Create(Guid id, string name, DateTimeOffset now)
    {
        return new Company
        {
            Id = id,
            Name = name,
            Status = CompanyStatus.PendingVerification,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, DateTimeOffset now)
    {
        Name = name;
        UpdatedAt = now;
    }

    public void SetAddress(CompanyAddress address, DateTimeOffset now)
    {
        var existingAddress = _addresses
            .SingleOrDefault(existing => existing.Type == address.Type);

        if (existingAddress is null)
        {
            _addresses.Add(address);
            UpdatedAt = now;
            return;
        }

        existingAddress.Update(
            address.Line1,
            address.Line2,
            address.City,
            address.Region,
            address.PostalCode,
            address.CountryCode,
            now);

        UpdatedAt = now;
    }

    public void SetSettings(CompanySettings settings, DateTimeOffset now)
    {
        Settings = settings;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        Status = CompanyStatus.Active;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        Status = CompanyStatus.Deactivated;
        UpdatedAt = now;
    }
}
