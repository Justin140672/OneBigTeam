namespace HR.Modules.Companies.Domain;

internal sealed class Company
{
    private readonly List<CompanyAddress> _addresses = [];

    private Company() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public CompanySettings? Settings { get; private set; }
    public CompanyBranding? Branding { get; private set; }
    public IReadOnlyCollection<CompanyAddress> Addresses => _addresses;

    public static Company Create(Guid id, string name, string slug, DateTimeOffset now)
    {
        return new Company
        {
            Id = id,
            Name = name,
            Slug = slug,
            IsActive = true,
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

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }
}
