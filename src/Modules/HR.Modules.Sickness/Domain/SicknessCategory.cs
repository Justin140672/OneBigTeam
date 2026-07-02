namespace HR.Modules.Sickness.Domain;

internal sealed class SicknessCategory
{
    private SicknessCategory() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int DisplayOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static SicknessCategory Create(
        Guid id,
        Guid companyId,
        string name,
        int displayOrder,
        DateTimeOffset now)
    {
        return new SicknessCategory
        {
            Id = id,
            CompanyId = companyId,
            Name = name,
            IsActive = true,
            DisplayOrder = displayOrder,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(string name, int displayOrder, bool isActive, DateTimeOffset now)
    {
        Name = name;
        DisplayOrder = displayOrder;
        IsActive = isActive;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }
}
