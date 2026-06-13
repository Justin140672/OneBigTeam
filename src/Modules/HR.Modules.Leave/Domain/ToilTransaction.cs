namespace HR.Modules.Leave.Domain;

internal sealed class ToilTransaction
{
    private ToilTransaction() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid LeaveBalanceId { get; private set; }
    public Guid AwardedByEmployeeId { get; private set; }
    public decimal Days { get; private set; }
    public DateOnly OccurredOn { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ToilTransaction Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid leaveBalanceId,
        Guid awardedByEmployeeId,
        decimal days,
        DateOnly occurredOn,
        string? notes,
        DateTimeOffset now)
    {
        return new ToilTransaction
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveBalanceId = leaveBalanceId,
            AwardedByEmployeeId = awardedByEmployeeId,
            Days = days,
            OccurredOn = occurredOn,
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
