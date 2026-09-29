using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
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
    IIntegrationEventPublisher integrationEventPublisher,
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

        var offboardingStatus = await offboardingStatusReader.GetStatusAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);
        var offboardingAlreadyStarted = offboardingStatus is not null;

        var now = clock.UtcNowOffset();

        leavingProcess.Amend(request.LeavingDate, request.LastWorkingDay, request.LeavingReason, now, request.Notes);

        // Ticket 2: optimistic concurrency (base-code helper). Nothing commits on conflict, so the
        // audit/integration events and departure finalisation below only run on a successful save.
        var saveResult = await dbContext.SaveChangesWithConcurrencyAsync(
            leavingProcess,
            request.ExpectedVersion,
            "This leaving process was changed by someone else since you opened it. Reload the latest details and try again.",
            cancellationToken);

        if (saveResult.IsFailure)
            return Result.Failure<AmendLeavingProcessResponse>(saveResult.Error);

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

        await integrationEventPublisher.PublishAsync(
            new EmployeeLeavingDateSetIntegrationEvent(
                leavingProcess.CompanyId, leavingProcess.EmployeeId,
                leavingProcess.LeavingDate, leavingProcess.LastWorkingDay, now),
            cancellationToken);

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
            leavingProcess.Notes,
            leavingProcess.Status.ToString(),
            offboardingAlreadyStarted,
            leavingProcess.Version));
    }
}
