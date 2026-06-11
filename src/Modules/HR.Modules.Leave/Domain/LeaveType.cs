namespace HR.Modules.Leave.Domain;

internal sealed class LeaveType
{
    private LeaveType() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public int DefaultEntitlementDays { get; private set; }
    public AccrualMethod AccrualMethod { get; private set; }
    public LeaveTypeBehaviour Behaviour { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static LeaveType Create(
        Guid id,
        Guid companyId,
        string name,
        string code,
        int defaultEntitlementDays,
        AccrualMethod accrualMethod,
        LeaveTypeBehaviour behaviour,
        DateTimeOffset now)
    {
        return new LeaveType
        {
            Id = id,
            CompanyId = companyId,
            Name = name,
            Code = code.ToUpperInvariant(),
            DefaultEntitlementDays = defaultEntitlementDays,
            AccrualMethod = accrualMethod,
            Behaviour = behaviour,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(string name, string code, int defaultEntitlementDays, AccrualMethod accrualMethod, LeaveTypeBehaviour behaviour, DateTimeOffset now)
    {
        Name = name;
        Code = code.ToUpperInvariant();
        DefaultEntitlementDays = defaultEntitlementDays;
        AccrualMethod = accrualMethod;
        Behaviour = behaviour;
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
