namespace HR.Modules.Leave.Domain;

internal sealed class LeaveBalanceAdjustment
{
    private LeaveBalanceAdjustment() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid LeaveTypeId { get; private set; }
    public decimal AdjustmentHours { get; private set; }
    public LeaveBalanceAdjustmentReason Reason { get; private set; }
    public string? Comments { get; private set; }
    public Guid AdjustedByEmployeeId { get; private set; }
    public DateTimeOffset AdjustedAt { get; private set; }

    public static LeaveBalanceAdjustment Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid leaveTypeId,
        decimal adjustmentHours,
        LeaveBalanceAdjustmentReason reason,
        string? comments,
        Guid adjustedByEmployeeId,
        DateTimeOffset adjustedAt)
    {
        return new LeaveBalanceAdjustment
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            AdjustmentHours = adjustmentHours,
            Reason = reason,
            Comments = comments,
            AdjustedByEmployeeId = adjustedByEmployeeId,
            AdjustedAt = adjustedAt
        };
    }
}
