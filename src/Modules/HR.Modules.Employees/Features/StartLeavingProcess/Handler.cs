using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.StartLeavingProcess;

internal sealed class StartLeavingProcessHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    ICompanyTimeZoneReader companyTimeZoneReader,
    IEffectiveNoticePeriodResolver noticePeriodResolver,
    IAuditEventPublisher auditEventPublisher,
    IIntegrationEventPublisher integrationEventPublisher,
    INotificationWriter notificationWriter,
    IOffboardingPlanCoordinator offboardingPlanCoordinator,
    IEmployeeDepartureFinalizer departureFinalizer)
{
    public async Task<Result<StartLeavingProcessResponse>> HandleAsync(
        StartLeavingProcessRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, StartLeavingProcessResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<StartLeavingProcessResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var employee = await dbContext.Employees
            .SingleOrDefaultAsync(
                e => e.Id == request.EmployeeId && e.CompanyId == request.CompanyId,
                cancellationToken);

        if (employee is null)
            return Result.Failure<StartLeavingProcessResponse>(
                Error.NotFound($"Employee '{request.EmployeeId}' was not found."));

        var hasActiveLeavingProcess = await dbContext.EmployeeLeavingProcesses
            .AnyAsync(
                p => p.CompanyId == request.CompanyId
                    && p.EmployeeId == request.EmployeeId
                    && p.Status == LeavingProcessStatus.InProgress,
                cancellationToken);

        if (hasActiveLeavingProcess)
            return Result.Failure<StartLeavingProcessResponse>(
                Error.Conflict("A leaving process is already in progress for this employee."));

        var timeZoneId = await companyTimeZoneReader.GetTimeZoneAsync(request.CompanyId, cancellationToken);
        var today = clock.TodayIn(timeZoneId);
        var isBackdated = request.LeavingDate < today;

        if (isBackdated && !request.ConfirmBackdatedLeavingDate)
            return Result.Failure<StartLeavingProcessResponse>(
                Error.Conflict(
                    "LeavingDate is in the past. Confirm to backdate and finalise the employee's departure immediately."));

        if (request.ReplacementManagerEmployeeId is not null)
        {
            var replacementManagerExists = await dbContext.Employees
                .AnyAsync(
                    e => e.Id == request.ReplacementManagerEmployeeId
                        && e.CompanyId == request.CompanyId
                        && e.Status != EmploymentStatus.FormerEmployee,
                    cancellationToken);

            if (!replacementManagerExists)
                return Result.Failure<StartLeavingProcessResponse>(
                    Error.NotFound($"Replacement manager '{request.ReplacementManagerEmployeeId}' was not found."));
        }

        var positionProfileOverrides = await dbContext.PositionProfiles
            .Where(p => p.Id == employee.PositionProfileId)
            .Select(p => new { p.NoticePeriodUnitOverride, p.NoticePeriodLengthOverride })
            .FirstOrDefaultAsync(cancellationToken);

        var effectiveNoticePeriod = await noticePeriodResolver.ResolveAsync(
            request.CompanyId,
            employee.NoticePeriodUnitOverride,
            employee.NoticePeriodLengthOverride,
            positionProfileOverrides?.NoticePeriodUnitOverride,
            positionProfileOverrides?.NoticePeriodLengthOverride,
            cancellationToken);

        var now = clock.UtcNowOffset();

        var leavingProcess = EmployeeLeavingProcess.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.ResignationReceivedDate,
            request.LeavingDate,
            request.LastWorkingDay,
            effectiveNoticePeriod.Unit,
            effectiveNoticePeriod.Length,
            effectiveNoticePeriod.Source,
            request.LeavingReason,
            actorEmployeeId,
            now,
            request.ReplacementManagerEmployeeId,
            request.Notes);

        dbContext.EmployeeLeavingProcesses.Add(leavingProcess);

        employee.SetLeaving(now);

        StartLeavingProcessResponse BuildResponse() => new(
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
            leavingProcess.StartedAt);

        var response = BuildResponse();

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync<IdempotencyRecord, StartLeavingProcessResponse>(dbContext.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await offboardingPlanCoordinator.StartAsync(
            request.CompanyId, request.EmployeeId, request.LastWorkingDay, notes: null,
            request.ReplacementManagerEmployeeId, cancellationToken);

        await auditEventPublisher.PublishAsync(
            new LeavingProcessStartedAuditEvent(
                leavingProcess.CompanyId,
                leavingProcess.EmployeeId,
                leavingProcess.Id,
                actorEmployeeId,
                now,
                new LeavingProcessSnapshot(
                    leavingProcess.ResignationReceivedDate,
                    leavingProcess.LeavingDate,
                    leavingProcess.LastWorkingDay,
                    leavingProcess.NoticePeriodUnit,
                    leavingProcess.NoticePeriodLength,
                    leavingProcess.NoticeSource,
                    leavingProcess.LeavingReason,
                    leavingProcess.Status)),
            cancellationToken);

        await NotifyLeavingProcessStartedAsync(employee, leavingProcess, now, cancellationToken);

        await integrationEventPublisher.PublishAsync(
            new EmployeeLeavingDateSetIntegrationEvent(
                leavingProcess.CompanyId, leavingProcess.EmployeeId,
                leavingProcess.LeavingDate, leavingProcess.LastWorkingDay, now),
            cancellationToken);

        if (isBackdated)
            await departureFinalizer.FinalizeAsync(employee, leavingProcess, now, cancellationToken);

        return Result.Success(BuildResponse());
    }

    private async Task NotifyLeavingProcessStartedAsync(
        Employee employee,
        EmployeeLeavingProcess leavingProcess,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (employee.ManagerId.HasValue)
        {
            await notificationWriter.WriteAsync(
                Guid.NewGuid(), leavingProcess.CompanyId, employee.ManagerId.Value,
                $"Leaving process started for {PersonName.Display(employee.FirstName, employee.LastName, employee.PreferredName)}",
                $"{PersonName.Display(employee.FirstName, employee.LastName, employee.PreferredName)}'s leaving process has been started. Their last working day is {leavingProcess.LastWorkingDay:d}.",
                leavingProcess.Id,
                NotificationType.LeavingProcessStarted,
                NotificationPriority.Normal,
                now,
                cancellationToken);
        }

        await notificationWriter.WriteAsync(
            Guid.NewGuid(), leavingProcess.CompanyId, leavingProcess.EmployeeId,
            "Your leaving process has started",
            $"Your leaving process has been started. Your last working day is {leavingProcess.LastWorkingDay:d}.",
            leavingProcess.Id,
            NotificationType.LeavingProcessStarted,
            NotificationPriority.Normal,
            now,
            cancellationToken);
    }
}
