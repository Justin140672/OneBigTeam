using HR.SharedKernel;

namespace HR.Modules.Leave.Domain;

internal sealed class LeaveType : IVersionedAggregate
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

    public bool HasBalance { get; private set; }

    public bool IsSystem { get; private set; }

    public int? ToilExpiryDays { get; private set; }

    /// <summary>
    /// TOIL-only policy setting (LEAVE-06): whether an approval that would take the employee's TOIL
    /// balance negative is permitted. Defaults to false - unlike entitled leave types (which may
    /// allow negative balances via <see cref="LeavePolicy.AllowNegativeBalance"/>), earned TOIL
    /// going negative is a distinct, deliberately conservative judgement call and is opt-in only.
    /// </summary>
    public bool AllowNegativeToilBalance { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Explicit, persisted optimistic-concurrency token (Ticket 2). See Employee.Version.
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

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
        bool isSystem = false,
        int? toilExpiryDays = null,
        bool allowNegativeToilBalance = false)
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
            ToilExpiryDays = toilExpiryDays,
            AllowNegativeToilBalance = allowNegativeToilBalance,
            Version = 1,
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
        bool hasBalance = true,
        int? toilExpiryDays = null,
        bool allowNegativeToilBalance = false)
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
        ToilExpiryDays = toilExpiryDays;
        AllowNegativeToilBalance = allowNegativeToilBalance;
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
