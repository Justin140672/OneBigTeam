using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CancelLeavingProcess;

internal sealed class CancelLeavingProcessHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    LeavingProcessPropagationService propagationService,
    IOffboardingStatusReader offboardingStatusReader,
    IOffboardingPlanCoordinator offboardingPlanCoordinator)
{
    public async Task<Result<CancelLeavingProcessResponse>> HandleAsync(
        CancelLeavingProcessRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, CancelLeavingProcessResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    await propagationService.TryDispatchForEmployeeAsync(
                        request.CompanyId, request.EmployeeId, cancellationToken);
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CancelLeavingProcessResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var leavingProcess = await dbContext.EmployeeLeavingProcesses
            .SingleOrDefaultAsync(
                p => p.CompanyId == request.CompanyId
                    && p.EmployeeId == request.EmployeeId
                    && p.Status == LeavingProcessStatus.InProgress,
                cancellationToken);

        if (leavingProcess is null)
            return Result.Failure<CancelLeavingProcessResponse>(
                Error.NotFound($"No in-progress leaving process was found for employee '{request.EmployeeId}'."));

        var employee = await dbContext.Employees
            .SingleOrDefaultAsync(
                e => e.Id == request.EmployeeId && e.CompanyId == request.CompanyId,
                cancellationToken);

        if (employee is null)
            return Result.Failure<CancelLeavingProcessResponse>(
                Error.NotFound($"Employee '{request.EmployeeId}' was not found."));

        var offboardingStatus = await offboardingStatusReader.GetStatusAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);
        var offboardingAlreadyStarted = offboardingStatus is not null;

        var now = clock.UtcNowOffset();

        leavingProcess.Cancel(request.CancellationReason, now);
        employee.Activate(now);

        propagationService.Stage(leavingProcess, LeavingProcessPropagation.OperationCancelled, now);

        var response = new CancelLeavingProcessResponse(
            leavingProcess.Id,
            leavingProcess.CompanyId,
            leavingProcess.EmployeeId,
            leavingProcess.Status.ToString(),
            offboardingAlreadyStarted);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync<IdempotencyRecord, CancelLeavingProcessResponse>(dbContext.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
            {
                await propagationService.TryDispatchForEmployeeAsync(
                    request.CompanyId, request.EmployeeId, cancellationToken);
                return Result.Success(outcome.Response!);
            }
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (offboardingAlreadyStarted)
        {
            await offboardingPlanCoordinator.CancelOutstandingTasksAsync(
                request.CompanyId, request.EmployeeId, cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new LeavingProcessCancelledAuditEvent(
                leavingProcess.CompanyId,
                leavingProcess.EmployeeId,
                leavingProcess.Id,
                actorEmployeeId,
                now,
                request.CancellationReason,
                offboardingAlreadyStarted),
            cancellationToken);

        await propagationService.TryDispatchForEmployeeAsync(
            leavingProcess.CompanyId, leavingProcess.EmployeeId, cancellationToken);

        return Result.Success(response);
    }
}
