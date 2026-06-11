namespace HR.Modules.Leave.Domain;

internal sealed class LeaveRequest
{
    private LeaveRequest() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid LeaveTypeId { get; private set; }
    public Guid? LeavePolicyId { get; private set; }
    public LeaveStatus Status { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public decimal TotalDays { get; private set; }
    public string? Notes { get; private set; }
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
        DateOnly endDate,
        decimal totalDays,
        string? notes,
        DateTimeOffset now)
    {
        return new LeaveRequest
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            LeavePolicyId = leavePolicyId,
            Status = LeaveStatus.Pending,
            StartDate = startDate,
            EndDate = endDate,
            TotalDays = totalDays,
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Approve(Guid reviewedByEmployeeId, DateTimeOffset now)
    {
        Status = LeaveStatus.Approved;
        ReviewedByEmployeeId = reviewedByEmployeeId;
        ReviewedAt = now;
        UpdatedAt = now;
    }

    public void Reject(Guid reviewedByEmployeeId, DateTimeOffset now)
    {
        Status = LeaveStatus.Rejected;
        ReviewedByEmployeeId = reviewedByEmployeeId;
        ReviewedAt = now;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        Status = LeaveStatus.Cancelled;
        UpdatedAt = now;
    }

    public void UpdateDetails(
        Guid leaveTypeId,
        DateOnly startDate,
        DateOnly endDate,
        decimal totalDays,
        string? notes,
        DateTimeOffset now)
    {
        LeaveTypeId = leaveTypeId;
        StartDate = startDate;
        EndDate = endDate;
        TotalDays = totalDays;
        Notes = notes;
        UpdatedAt = now;
    }
}
