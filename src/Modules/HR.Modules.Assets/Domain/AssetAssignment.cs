namespace HR.Modules.Assets.Domain;

internal sealed class AssetAssignment
{
    private AssetAssignment() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AssetId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid AssignedBy { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public DateTimeOffset? ReturnedAt { get; private set; }
    public AssetAssignmentReturnOutcome? ReturnOutcome { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => ReturnedAt is null;

    public static AssetAssignment Create(
        Guid id,
        Guid companyId,
        Guid assetId,
        Guid employeeId,
        Guid assignedBy,
        string? notes,
        DateTimeOffset now)
    {
        return new AssetAssignment
        {
            Id = id,
            CompanyId = companyId,
            AssetId = assetId,
            EmployeeId = employeeId,
            AssignedBy = assignedBy,
            AssignedAt = now,
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Acknowledge(DateTimeOffset now)
    {
        if (AcknowledgedAt is not null)
            throw new InvalidOperationException("Asset assignment has already been acknowledged.");

        AcknowledgedAt = now;
        UpdatedAt = now;
    }

    public void Return(DateTimeOffset now, AssetAssignmentReturnOutcome outcome = AssetAssignmentReturnOutcome.Returned)
    {
        if (ReturnedAt is not null)
            throw new InvalidOperationException("Asset has already been returned.");

        ReturnedAt = now;
        ReturnOutcome = outcome;
        UpdatedAt = now;
    }
}
