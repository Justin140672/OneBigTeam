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

    /// <summary>
    /// LEAVE-07: whether leave requests submitted under this policy require manual approval
    /// (Pending -> Approve/Reject) or are approved automatically at submission time. Defaults to
    /// true — the safer default per LEAVE-07 design guidance — so a policy created without an
    /// explicit choice never silently auto-approves leave.
    /// </summary>
    public bool RequiresApproval { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static LeavePolicy Create(
        Guid id,
        Guid companyId,
        string name,
        string? description,
        int carryOverDays,
        bool allowNegativeBalance,
        bool isDefault,
        DateTimeOffset now,
        bool requiresApproval = true)
    {
        return new LeavePolicy
        {
            Id = id,
            CompanyId = companyId,
            Name = name,
            Description = description,
            CarryOverDays = carryOverDays,
            AllowNegativeBalance = allowNegativeBalance,
            RequiresApproval = requiresApproval,
            IsActive = true,
            IsDefault = isDefault,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(
        string name,
        string? description,
        int carryOverDays,
        bool allowNegativeBalance,
        bool requiresApproval,
        DateTimeOffset now)
    {
        Name = name;
        Description = description;
        CarryOverDays = carryOverDays;
        AllowNegativeBalance = allowNegativeBalance;
        RequiresApproval = requiresApproval;
        UpdatedAt = now;
    }

    // NOTE: When a Deactivate feature is eventually added for this entity, it must block
    // deactivating the company's default leave policy — no caller exists yet, so that guard
    // is intentionally not implemented here.
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

    public void MarkAsDefault(DateTimeOffset now)
    {
        IsDefault = true;
        UpdatedAt = now;
    }

    public void UnmarkAsDefault(DateTimeOffset now)
    {
        IsDefault = false;
        UpdatedAt = now;
    }
}
