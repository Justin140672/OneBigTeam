namespace HR.Modules.Assets.Domain;

internal sealed class AssetCategory
{
    private AssetCategory() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AssetCategory Create(
        Guid id,
        Guid companyId,
        string name,
        string? description,
        DateTimeOffset now)
    {
        return new AssetCategory
        {
            Id = id,
            CompanyId = companyId,
            Name = name,
            Description = description,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(string name, string? description, DateTimeOffset now)
    {
        Name = name;
        Description = description;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }
}
