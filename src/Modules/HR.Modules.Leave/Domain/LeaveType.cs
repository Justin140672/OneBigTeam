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

    /// <summary>
    /// Leave-type-level configuration flag: whether this type of leave tracks a balance
    /// (entitlement/used/adjustment/remaining) at all. Independent of <see cref="Behaviour"/> —
    /// e.g. an Unpaid Leave type would typically have this set to false, but it is not derived
    /// from Behaviour and can be set independently. When false, employees never get a
    /// <c>LeaveBalance</c> row for this type and the balance UI renders "n/a" for it.
    /// </summary>
    public bool HasBalance { get; private set; }

    /// <summary>
    /// True for the platform-provisioned "Annual Leave" record (dev seed data and
    /// ILeaveTypeDefaultsProvisioner) — item 50's replacement for matching that record by its
    /// Name string. A system leave type can never be renamed or deactivated (see
    /// UpdateLeaveTypeHandler/DeactivateLeaveTypeHandler); other fields (code, default
    /// entitlement, accrual method, behaviour, tracks-balance) remain editable. Never true for a
    /// company-created leave type — there is no API surface to set this on Create.
    /// </summary>
    public bool IsSystem { get; private set; }

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
        DateTimeOffset now,
        bool hasBalance = true,
        bool isSystem = false)
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
            HasBalance = hasBalance,
            IsSystem = isSystem,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(
        string name,
        string code,
        int defaultEntitlementDays,
        AccrualMethod accrualMethod,
        LeaveTypeBehaviour behaviour,
        DateTimeOffset now,
        bool hasBalance = true)
    {
        // System leave types (e.g. Annual Leave) can never be renamed — see IsSystem's doc
        // comment. Callers (UpdateLeaveTypeHandler) are expected to reject a rename attempt
        // before calling Update, but this is enforced here too as the domain invariant of record.
        Name = IsSystem ? Name : name;
        Code = code.ToUpperInvariant();
        DefaultEntitlementDays = defaultEntitlementDays;
        AccrualMethod = accrualMethod;
        Behaviour = behaviour;
        HasBalance = hasBalance;
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
