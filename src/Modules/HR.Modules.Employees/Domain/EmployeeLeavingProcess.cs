using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Services;

namespace HR.Modules.Employees.Domain;

internal sealed class EmployeeLeavingProcess
{
    private EmployeeLeavingProcess() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly ResignationReceivedDate { get; private set; }
    public DateOnly LeavingDate { get; private set; }
    public DateOnly LastWorkingDay { get; private set; }
    public NoticePeriodUnit NoticePeriodUnit { get; private set; }
    public int NoticePeriodLength { get; private set; }
    public NoticePeriodSource NoticeSource { get; private set; }
    public LeavingReason LeavingReason { get; private set; }
    public LeavingProcessStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public Guid StartedByUserId { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static EmployeeLeavingProcess Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        DateOnly resignationReceivedDate,
        DateOnly leavingDate,
        DateOnly lastWorkingDay,
        NoticePeriodUnit noticePeriodUnit,
        int noticePeriodLength,
        NoticePeriodSource noticeSource,
        LeavingReason leavingReason,
        Guid startedByUserId,
        DateTimeOffset now)
    {
        return new EmployeeLeavingProcess
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            ResignationReceivedDate = resignationReceivedDate,
            LeavingDate = leavingDate,
            LastWorkingDay = lastWorkingDay,
            NoticePeriodUnit = noticePeriodUnit,
            NoticePeriodLength = noticePeriodLength,
            NoticeSource = noticeSource,
            LeavingReason = leavingReason,
            Status = LeavingProcessStatus.InProgress,
            StartedAt = now,
            StartedByUserId = startedByUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    // HR may amend the leaving date, last working day and reason while the process is still
    // in progress. Notice period figures (NoticePeriodUnit/Length/Source) are intentionally left
    // untouched here — once HR explicitly types a new LeavingDate, the original notice-period
    // calculation becomes historical/informational rather than something to silently re-derive.
    public void Amend(DateOnly leavingDate, DateOnly lastWorkingDay, LeavingReason leavingReason, DateTimeOffset now)
    {
        if (Status != LeavingProcessStatus.InProgress)
            throw new InvalidOperationException($"Cannot amend a leaving process with status '{Status}'.");

        LeavingDate = leavingDate;
        LastWorkingDay = lastWorkingDay;
        LeavingReason = leavingReason;
        UpdatedAt = now;
    }

    public void Cancel(string cancellationReason, DateTimeOffset now)
    {
        if (Status != LeavingProcessStatus.InProgress)
            throw new InvalidOperationException($"Cannot cancel a leaving process with status '{Status}'.");

        Status = LeavingProcessStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = cancellationReason;
        UpdatedAt = now;
    }

    // Called by ProcessLeavingEmployeesJob once the employee's leaving date has passed and the
    // departure has been finalised (status change, access disabling, offboarding check all done).
    public void Complete(DateTimeOffset now)
    {
        if (Status != LeavingProcessStatus.InProgress)
            throw new InvalidOperationException($"Cannot complete a leaving process with status '{Status}'.");

        Status = LeavingProcessStatus.Completed;
        UpdatedAt = now;
    }
}
