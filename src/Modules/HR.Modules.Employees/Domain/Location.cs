namespace HR.Modules.Employees.Domain;

internal sealed class Location
{
    private Location() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid LocationTypeId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Location Create(
        Guid id,
        Guid companyId,
        Guid locationTypeId,
        string name,
        string? description,
        DateTimeOffset now)
    {
        return new Location
        {
            Id = id,
            CompanyId = companyId,
            LocationTypeId = locationTypeId,
            Name = name,
            Description = description,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        string name,
        string? description,
        Guid locationTypeId,
        DateTimeOffset now)
    {
        Name = name;
        Description = description;
        LocationTypeId = locationTypeId;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }
}
