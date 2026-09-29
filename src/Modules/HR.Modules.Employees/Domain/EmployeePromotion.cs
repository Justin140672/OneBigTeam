namespace HR.Modules.Employees.Domain;

internal sealed class EmployeePromotion
{
    private EmployeePromotion() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid PreviousPositionProfileId { get; private set; }
    public Guid NewPositionProfileId { get; private set; }
    public Guid? NewManagerId { get; private set; }
    public Guid? NewLocationId { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public string Reason { get; private set; } = null!;
    public string? Notes { get; private set; }
    public Guid? CompensationId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    // Internal recruitment Ticket 7: the department the employee moves to when this promotion is
    // finalised, taken from the new position profile's own department. Null for promotions recorded
    // before this column existed; the employee's department is then left unchanged (the previous
    // behaviour).
    public Guid? NewDepartmentId { get; private set; }

    // Internal recruitment Ticket 7: NewManagerId == null has always meant "keep the current
    // manager". An internal appointment can deliberately leave the employee with no manager, so that
    // intent is persisted explicitly rather than overloading the null.
    public bool ClearsManager { get; private set; }

    // Internal recruitment Ticket 7: stable idempotency key for promotions recorded as a side effect of
    // another module's workflow ("recruitment:application:{id}" for an internal appointment). The
    // filtered unique index on (company_id, source_reference) makes a duplicate impossible, so a
    // retried appointment returns this promotion instead of recording a second change. Null for
    // promotions entered directly by HR.
    public string? SourceReference { get; private set; }

    public const string InternalAppointmentSourcePrefix = "recruitment:application:";

    public bool IsInternalAppointment =>
        SourceReference is not null &&
        SourceReference.StartsWith(InternalAppointmentSourcePrefix, StringComparison.Ordinal);

    public Guid? ResolveManagerId(Guid? currentManagerId) =>
        ClearsManager ? null : NewManagerId ?? currentManagerId;

    public static EmployeePromotion Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid previousPositionProfileId,
        Guid newPositionProfileId,
        Guid? newManagerId,
        Guid? newLocationId,
        DateOnly effectiveDate,
        string reason,
        string? notes,
        Guid? compensationId,
        Guid createdBy,
        DateTimeOffset now,
        Guid? newDepartmentId = null,
        bool clearsManager = false,
        string? sourceReference = null)
    {
        if (clearsManager && newManagerId is not null)
            throw new ArgumentException("A promotion cannot both clear the manager and assign a new one.", nameof(clearsManager));

        return new EmployeePromotion
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            PreviousPositionProfileId = previousPositionProfileId,
            NewPositionProfileId = newPositionProfileId,
            NewManagerId = newManagerId,
            NewLocationId = newLocationId,
            EffectiveDate = effectiveDate,
            Reason = reason,
            Notes = notes,
            CompensationId = compensationId,
            CreatedBy = createdBy,
            CreatedDate = now,
            NewDepartmentId = newDepartmentId,
            ClearsManager = clearsManager,
            SourceReference = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim(),
        };
    }

    public void Complete(DateTimeOffset now)
    {
        if (CompletedAt.HasValue)
            throw new InvalidOperationException("Cannot complete a promotion that has already been completed.");

        CompletedAt = now;
    }
}
