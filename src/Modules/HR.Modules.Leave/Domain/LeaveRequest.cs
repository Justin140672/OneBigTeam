namespace HR.Modules.Leave.Domain;

internal sealed class LeaveRequest
{
    private LeaveRequest() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid LeaveTypeId { get; private set; }
    public Guid? LeavePolicyId { get; private set; }
    public LeaveRequestStatus Status { get; private set; }
    public DateOnly StartDate { get; private set; }
    public LeaveDayPart StartPart { get; private set; }
    public DateOnly EndDate { get; private set; }
    public LeaveDayPart EndPart { get; private set; }
    public decimal TotalDays { get; private set; }
    public string? Reason { get; private set; }
    public Guid? ReviewedByEmployeeId { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static LeaveRequest Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid leaveTypeId,
        Guid? leavePolicyId,
        DateOnly startDate,
        LeaveDayPart startPart,
        DateOnly endDate,
        LeaveDayPart endPart,
        decimal totalDays,
        string? reason,
        DateTimeOffset now)
    {
        return new LeaveRequest
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            LeavePolicyId = leavePolicyId,
            Status = LeaveRequestStatus.Pending,
            StartDate = startDate,
            StartPart = startPart,
            EndDate = endDate,
            EndPart = endPart,
            TotalDays = totalDays,
            Reason = reason,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Approve(Guid reviewedByEmployeeId, DateTimeOffset now)
    {
        Status = LeaveRequestStatus.Approved;
        ReviewedByEmployeeId = reviewedByEmployeeId;
        ReviewedAt = now;
        UpdatedAt = now;
    }

    public void Reject(Guid reviewedByEmployeeId, DateTimeOffset now)
    {
        Status = LeaveRequestStatus.Rejected;
        ReviewedByEmployeeId = reviewedByEmployeeId;
        ReviewedAt = now;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        Status = LeaveRequestStatus.Cancelled;
        UpdatedAt = now;
    }

    public void UpdateDetails(
        Guid leaveTypeId,
        DateOnly startDate,
        LeaveDayPart startPart,
        DateOnly endDate,
        LeaveDayPart endPart,
        decimal totalDays,
        string? reason,
        DateTimeOffset now)
    {
        LeaveTypeId = leaveTypeId;
        StartDate = startDate;
        StartPart = startPart;
        EndDate = endDate;
        EndPart = endPart;
        TotalDays = totalDays;
        Reason = reason;
        UpdatedAt = now;
    }
}
