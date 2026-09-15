using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Services;
using HR.SharedKernel;

namespace HR.Modules.Employees.Domain;

internal sealed class EmployeeLeavingProcess : IVersionedAggregate
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

    // OFF-06: the manager HR has nominated to take over this employee's direct reports (and any
    // pending manager-scoped approvals/reviews assigned to this employee) once their departure is
    // finalised. Optional — when null, direct reports are left without a manager and the
    // departure is routed to an HR exception queue instead (see
    // EmployeeDepartureFinalizer/StartOffboardingHandler). Only meaningful when this employee
    // actually has direct reports; otherwise it is simply unused.
    public Guid? ReplacementManagerEmployeeId { get; private set; }

    // Set only once every downstream finalisation step (offboarding-completeness check, manager
    // notification, audit publish, integration event publish, timeline write) in
    // EmployeeDepartureFinalizer has actually completed for this process — distinct from Status
    // becoming Completed, which happens earlier as soon as the terminal-state save succeeds. A
    // process can be Completed with this still null if the process crashed/threw between those two
    // points; ProcessLeavingEmployeesJob's reconciliation scan uses that gap to find and re-run the
    // missing downstream steps for a stranded departure.
    //
    // IMPORTANT — what this DOES and DOES NOT guarantee: this only means the in-process
    // IIntegrationEventPublisher.PublishAsync call for EmployeeDepartureFinalisedIntegrationEvent
    // returned control to EmployeeDepartureFinalizer. HR.SharedKernel.IntegrationEventPublisher
    // deliberately catches and only logs each handler's own exception (so one failing consumer never
    // blocks another consumer or this publishing call) — so this flag does NOT mean every consuming
    // module's downstream work actually completed. Any module with a required, must-not-be-lost
    // reaction to this event is responsible for its own durable tracking/retry of that reaction (see
    // HR.Modules.Identity's AccountDisablement/AccountDisablementJob for account disablement, and
    // HR.Modules.Leave's LeavePolicyDeactivationOnDeparture/LeavePolicyDeactivationJob for leave
    // policy deactivation) — Employees cannot and must not gate this flag on another module's
    // internal state, since that would require querying another module's schema directly.
    public DateTimeOffset? FinalisationCompletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Explicit, persisted optimistic-concurrency token (Ticket 2). See Employee.Version.
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

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
        DateTimeOffset now,
        Guid? replacementManagerEmployeeId = null)
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
            Version = 1,
            StartedAt = now,
            StartedByUserId = startedByUserId,
            ReplacementManagerEmployeeId = replacementManagerEmployeeId,
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

    // Called by EmployeeDepartureFinalizer once every downstream finalisation step has actually
    // succeeded. Guarded so a defensive/reconciliation re-invocation for a process that already
    // finished downstream work is a safe no-op rather than an error.
    public void MarkFinalisationCompleted(DateTimeOffset now)
    {
        if (Status != LeavingProcessStatus.Completed)
            throw new InvalidOperationException(
                $"Cannot mark finalisation completed for a leaving process with status '{Status}'.");

        if (FinalisationCompletedAt is not null)
            return;

        FinalisationCompletedAt = now;
        UpdatedAt = now;
    }
}
