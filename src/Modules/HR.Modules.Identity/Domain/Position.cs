namespace HR.Modules.Identity.Domain;

internal sealed class Position
{
    private Position() { }

    public Guid Id { get; private set; }
    public string TenantId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Position Create(Guid id, string tenantId, string name, DateTimeOffset now)
    {
        return new Position
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = name;
        NormalizedName = name.ToUpperInvariant();
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }
}
