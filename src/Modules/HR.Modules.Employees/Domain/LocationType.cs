using HR.SharedKernel;

namespace HR.Modules.Employees.Domain;

internal sealed class LocationType : IVersionedAggregate
{
    private LocationType() { }

    public Guid   Id          { get; private set; }
    public Guid   CompanyId   { get; private set; }
    public string Name        { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool   IsActive    { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Explicit, persisted optimistic-concurrency token (Ticket 2). See Employee.Version.
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

    public static LocationType Create(Guid id, Guid companyId, string name, string? description, DateTimeOffset now) =>
        new()
        {
            Id          = id,
            CompanyId   = companyId,
            Name        = name,
            Description = description,
            IsActive    = true,
            Version     = 1,
            CreatedAt   = now,
            UpdatedAt   = now,
        };

    public void Update(string name, string? description, DateTimeOffset now)
    {
        Name        = name;
        Description = description;
        UpdatedAt   = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive  = false;
        UpdatedAt = now;
    }
}
