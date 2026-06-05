namespace HR.Modules.Companies.Domain;

internal sealed class Company
{
    private Company() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

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

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }
}
