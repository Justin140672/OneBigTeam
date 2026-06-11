namespace HR.Modules.Leave.Domain;

internal sealed class LeaveRequest
{
    private LeaveRequest() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public LeaveType LeaveType { get; private set; }
    public LeaveStatus Status { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string? Notes { get; private set; }
    public Guid? ReviewedByEmployeeId { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static LeaveRequest Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        LeaveType leaveType,
        DateOnly startDate,
        DateOnly endDate,
        string? notes,
        DateTimeOffset now)
    {
        return new LeaveRequest
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveType = leaveType,
            Status = LeaveStatus.Pending,
            StartDate = startDate,
            EndDate = endDate,
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
        LeaveType leaveType,
        DateOnly startDate,
        DateOnly endDate,
        string? notes,
        DateTimeOffset now)
    {
        LeaveType = leaveType;
        StartDate = startDate;
        EndDate = endDate;
        Notes = notes;
        UpdatedAt = now;
    }
}
