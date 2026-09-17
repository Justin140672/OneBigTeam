using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Features.WaiveOffboardingTask;

internal sealed class WaiveOffboardingTaskHandler(
    OffboardingDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<WaiveOffboardingTaskResponse>> HandleAsync(
        WaiveOffboardingTaskRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var offboardingTask = await dbContext.OffboardingTasks
            .FirstOrDefaultAsync(
                t => t.Id == request.OffboardingTaskId && t.CompanyId == request.CompanyId,
                cancellationToken);

        if (offboardingTask is null)
            return Result.Failure<WaiveOffboardingTaskResponse>(
                Error.NotFound("The offboarding task was not found."));

        try
        {
            offboardingTask.Waive(clock.UtcNowOffset(), request.Reason, actorEmployeeId);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<WaiveOffboardingTaskResponse>(Error.Conflict(ex.Message));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Spec SPEC-OFF-01: EmployeeId is the leaving employee the plan belongs to — falls back to
        // the task's own AssignedEmployeeId in the unexpected case the owning plan cannot be found,
        // mirroring CompleteOffboardingTaskFromTaskAction's identical fallback for its own audit
        // events.
        var plan = await dbContext.OffboardingPlans
            .FirstOrDefaultAsync(p => p.Id == offboardingTask.OffboardingPlanId, cancellationToken);

        await auditEventPublisher.PublishAsync(
            new OffboardingTaskWaivedAuditEvent(
                offboardingTask.CompanyId,
                offboardingTask.OffboardingPlanId,
                offboardingTask.Id,
                plan?.EmployeeId ?? offboardingTask.AssignedEmployeeId ?? actorEmployeeId,
                actorEmployeeId,
                offboardingTask.Title,
                offboardingTask.SkipReason!,
                offboardingTask.SkippedAt!.Value),
            cancellationToken);

        return Result.Success(new WaiveOffboardingTaskResponse(
            offboardingTask.Id,
            offboardingTask.Status.ToString(),
            offboardingTask.SkippedAt,
            offboardingTask.SkippedByUserId));
    }
}
