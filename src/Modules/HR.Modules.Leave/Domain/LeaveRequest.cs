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
    public string? RejectionReason { get; private set; }
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
        DateTimeOffset now,
        LeaveRequestStatus status = LeaveRequestStatus.Pending)
    {
        return new LeaveRequest
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            LeavePolicyId = leavePolicyId,
            Status = status,
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

    /// <summary>
    /// Creates a Draft leave request (LEAVE-07). Drafts skip every blocking check that a real
    /// submission enforces (cross-year rejection, balance sufficiency, conflict detection) — see
    /// CreateLeaveRequestDraftHandler. TotalDays is still computed for display purposes only and
    /// is recalculated authoritatively when the draft is submitted.
    /// </summary>
    public static LeaveRequest CreateDraft(
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
        => Create(id, companyId, employeeId, leaveTypeId, leavePolicyId, startDate, startPart, endDate, endPart,
            totalDays, reason, now, LeaveRequestStatus.Draft);

    /// <summary>
    /// Re-resolves the policy a Draft is submitted under (LEAVE-07). A draft can be created
    /// before the employee has a resolvable policy assignment (LeavePolicyId null); submission
    /// re-queries the assignment fresh, so this lets SubmitLeaveRequestDraftHandler keep the
    /// stored LeavePolicyId in sync with what was actually used for the approval decision.
    /// </summary>
    public void AssignLeavePolicy(Guid? leavePolicyId)
    {
        LeavePolicyId = leavePolicyId;
    }

    public void Approve(Guid reviewedByEmployeeId, DateTimeOffset now)
    {
        Status = LeaveRequestStatus.Approved;
        ReviewedByEmployeeId = reviewedByEmployeeId;
        ReviewedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Transitions a Draft to Pending when the assigned leave policy requires manual approval.
    /// Callers (SubmitLeaveRequestDraftHandler) must verify Status == Draft beforehand and
    /// translate an invalid state into a Result failure — this throw is a defensive invariant,
    /// not the primary validation path (see 09-coding-standards.md: Result pattern for business
    /// flow, exceptions for unexpected failures only).
    /// </summary>
    public void MarkSubmittedPending(DateTimeOffset now)
    {
        if (Status != LeaveRequestStatus.Draft)
            throw new InvalidOperationException($"Cannot submit a leave request with status '{Status}'.");

        Status = LeaveRequestStatus.Pending;
        UpdatedAt = now;
    }

    public void Reject(Guid reviewedByEmployeeId, DateTimeOffset now, string? rejectionReason = null)
    {
        Status = LeaveRequestStatus.Rejected;
        ReviewedByEmployeeId = reviewedByEmployeeId;
        ReviewedAt = now;
        RejectionReason = rejectionReason;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        Status = LeaveRequestStatus.Cancelled;
        UpdatedAt = now;
    }

    /// <summary>
    /// Edits a Draft's details in place (LEAVE-07). Only callable while Status == Draft —
    /// submitted/approved/rejected/cancelled requests can never re-enter draft editing.
    /// UpdateLeaveRequestDraftHandler must check Status before calling; this throw is the
    /// domain-level backstop for that guard, not the primary validation path.
    /// </summary>
    public void UpdateDraftDetails(
        Guid leaveTypeId,
        DateOnly startDate,
        LeaveDayPart startPart,
        DateOnly endDate,
        LeaveDayPart endPart,
        decimal totalDays,
        string? reason,
        DateTimeOffset now)
    {
        if (Status != LeaveRequestStatus.Draft)
            throw new InvalidOperationException($"Cannot edit a leave request with status '{Status}' as a draft.");

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
