namespace HR.Modules.Leave.Domain;

internal sealed class LeavePolicy
{
    private LeavePolicy() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int CarryOverDays { get; private set; }
    public bool AllowNegativeBalance { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static LeavePolicy Create(
        Guid id,
        Guid companyId,
        string name,
        string? description,
        int carryOverDays,
        bool allowNegativeBalance,
        DateTimeOffset now)
    {
        return new LeavePolicy
        {
            Id = id,
            CompanyId = companyId,
            Name = name,
            Description = description,
            CarryOverDays = carryOverDays,
            AllowNegativeBalance = allowNegativeBalance,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(
        string name,
        string? description,
        int carryOverDays,
        bool allowNegativeBalance,
        DateTimeOffset now)
    {
        Name = name;
        Description = description;
        CarryOverDays = carryOverDays;
        AllowNegativeBalance = allowNegativeBalance;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }
}
