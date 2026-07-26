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
        DateTimeOffset now)
    {
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
        };
    }

    // Called by EmployeePromotionFinalizer once the promotion's effective date is due (or
    // immediately, when the effective date is today/backdated). The idempotency guard mirrors
    // EmployeeLeavingProcess.Complete exactly — the finalizer is responsible for not calling this
    // twice, not the entity swallowing a repeated call.
    public void Complete(DateTimeOffset now)
    {
        if (CompletedAt.HasValue)
            throw new InvalidOperationException("Cannot complete a promotion that has already been completed.");

        CompletedAt = now;
    }
}
