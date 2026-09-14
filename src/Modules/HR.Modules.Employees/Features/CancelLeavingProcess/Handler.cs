using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CancelLeavingProcess;

internal sealed class CancelLeavingProcessHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    IIntegrationEventPublisher integrationEventPublisher,
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

        // Reader returning null means no offboarding plan exists yet for this employee — the
        // "not yet begun" case (same semantics GetEmployeeHandler relies on for ShowOffboardingTab).
        // Any non-null status means a plan was created, which — per StartOffboardingHandler, which
        // always calls Start() immediately after Create() — means offboarding has already started.
        // The UI is responsible for confirming this with HR (with a stronger warning in that case)
        // before calling this endpoint at all; this handler just acts on the outcome.
        var offboardingStatus = await offboardingStatusReader.GetStatusAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);
        var offboardingAlreadyStarted = offboardingStatus is not null;

        var now = clock.UtcNowOffset();

        leavingProcess.Cancel(request.CancellationReason, now);
        employee.Activate(now);

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
                return Result.Success(outcome.Response!);
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

        // Cross-module notification so consuming modules (e.g. Leave, LEAVE-05) restore the
        // employee's current policy year entitlement to the figure it would have been had they
        // never entered the leaving process, while leaving any usage/manual adjustment recorded
        // during the leaving-pending period untouched.
        await integrationEventPublisher.PublishAsync(
            new EmployeeLeavingProcessCancelledIntegrationEvent(
                leavingProcess.CompanyId, leavingProcess.EmployeeId, now),
            cancellationToken);

        return Result.Success(response);
    }
}
