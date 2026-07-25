using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.AmendLeavingProcess;

internal sealed class AmendLeavingProcessHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    ICompanyTimeZoneReader companyTimeZoneReader,
    IAuditEventPublisher auditEventPublisher,
    IOffboardingStatusReader offboardingStatusReader,
    IEmployeeDepartureFinalizer departureFinalizer)
{
    public async Task<Result<AmendLeavingProcessResponse>> HandleAsync(
        AmendLeavingProcessRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var leavingProcess = await dbContext.EmployeeLeavingProcesses
            .SingleOrDefaultAsync(
                p => p.CompanyId == request.CompanyId
                    && p.EmployeeId == request.EmployeeId
                    && p.Status == LeavingProcessStatus.InProgress,
                cancellationToken);

        if (leavingProcess is null)
            return Result.Failure<AmendLeavingProcessResponse>(
                Error.NotFound($"No in-progress leaving process was found for employee '{request.EmployeeId}'."));

        // Backdating is permitted for genuine historical/corrective entry, but a LeavingDate
        // before today requires explicit confirmation since it immediately finalises the employee
        // through the same idempotent finalisation path ProcessLeavingEmployeesJob uses once a
        // leaving date becomes due.
        var timeZoneId = await companyTimeZoneReader.GetTimeZoneAsync(request.CompanyId, cancellationToken);
        var today = clock.TodayIn(timeZoneId);
        var isBackdated = request.LeavingDate < today;

        if (isBackdated && !request.ConfirmBackdatedLeavingDate)
            return Result.Failure<AmendLeavingProcessResponse>(
                Error.Conflict(
                    "LeavingDate is in the past. Confirm to backdate and finalise the employee's departure immediately."));

        var before = new LeavingProcessSnapshot(
            leavingProcess.ResignationReceivedDate,
            leavingProcess.LeavingDate,
            leavingProcess.LastWorkingDay,
            leavingProcess.NoticePeriodUnit,
            leavingProcess.NoticePeriodLength,
            leavingProcess.NoticeSource,
            leavingProcess.LeavingReason,
            leavingProcess.Status);

        // Surfaced back to the UI as a non-blocking warning per the AC ("warn if offboarding has
        // already started") — this endpoint amends the leaving process regardless of the result.
        var offboardingStatus = await offboardingStatusReader.GetStatusAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);
        var offboardingAlreadyStarted = offboardingStatus is not null;

        var now = clock.UtcNowOffset();

        leavingProcess.Amend(request.LeavingDate, request.LastWorkingDay, request.LeavingReason, now);

        await dbContext.SaveChangesAsync(cancellationToken);

        var after = new LeavingProcessSnapshot(
            leavingProcess.ResignationReceivedDate,
            leavingProcess.LeavingDate,
            leavingProcess.LastWorkingDay,
            leavingProcess.NoticePeriodUnit,
            leavingProcess.NoticePeriodLength,
            leavingProcess.NoticeSource,
            leavingProcess.LeavingReason,
            leavingProcess.Status);

        await auditEventPublisher.PublishAsync(
            new LeavingProcessAmendedAuditEvent(
                leavingProcess.CompanyId,
                leavingProcess.EmployeeId,
                leavingProcess.Id,
                actorEmployeeId,
                now,
                before,
                after,
                offboardingAlreadyStarted),
            cancellationToken);

        // request.ConfirmBackdatedLeavingDate is guaranteed true here — the unconfirmed case
        // already returned a Conflict above before the amendment was applied.
        if (isBackdated)
        {
            var employee = await dbContext.Employees
                .SingleAsync(e => e.Id == request.EmployeeId && e.CompanyId == request.CompanyId, cancellationToken);

            await departureFinalizer.FinalizeAsync(employee, leavingProcess, now, cancellationToken);
        }

        return Result.Success(new AmendLeavingProcessResponse(
            leavingProcess.Id,
            leavingProcess.CompanyId,
            leavingProcess.EmployeeId,
            leavingProcess.ResignationReceivedDate,
            leavingProcess.LeavingDate,
            leavingProcess.LastWorkingDay,
            leavingProcess.NoticePeriodUnit,
            leavingProcess.NoticePeriodLength,
            leavingProcess.NoticeSource.ToString(),
            leavingProcess.LeavingReason.ToString(),
            leavingProcess.Status.ToString(),
            offboardingAlreadyStarted));
    }
}
