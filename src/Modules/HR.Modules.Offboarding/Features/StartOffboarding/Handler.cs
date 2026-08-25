using HR.Modules.Tasks.Contracts;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Features.StartOffboarding;

internal sealed class StartOffboardingHandler(
    OffboardingDbContext dbContext,
    IClock clock,
    IEmployeeNameReader employeeNameReader,
    IManagerReader managerReader,
    IAssignedAssetReader assignedAssetReader,
    IOutstandingDocumentRequestReader documentReader,
    OffboardingTaskSynchronizer taskSynchronizer,
    INotificationWriter notificationWriter,
    IIntegrationEventPublisher integrationEventPublisher)
{
    public async Task<Result<StartOffboardingResponse>> HandleAsync(
        StartOffboardingRequest request,
        CancellationToken cancellationToken)
    {
        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, [request.EmployeeId], cancellationToken);
        if (!names.TryGetValue(request.EmployeeId, out var employeeNameValue))
            return Result.Failure<StartOffboardingResponse>(Error.NotFound("Employee not found."));

        var employeeName = string.IsNullOrEmpty(employeeNameValue) ? "the employee" : employeeNameValue;

        // Fast-path pre-check — avoids the round trip to build tasks and hit the database when a
        // conflict is already obviously true. Not the source of correctness under concurrency: see
        // the unique index / DbUpdateException handling below, which is the real guarantee.
        var hasActivePlan = await dbContext.OffboardingPlans
            .AnyAsync(
                p => p.CompanyId == request.CompanyId
                    && p.EmployeeId == request.EmployeeId
                    && p.Status != OffboardingStatus.Completed
                    && p.Status != OffboardingStatus.Cancelled,
                cancellationToken);

        if (hasActivePlan)
            return Result.Failure<StartOffboardingResponse>(
                Error.Conflict("An offboarding plan already exists for this employee."));

        var now = clock.UtcNowOffset();

        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), request.CompanyId, request.EmployeeId, request.LastWorkingDay, request.Notes, now);
        dbContext.OffboardingPlans.Add(plan);
        plan.Start(now);

        var managerId = await managerReader.GetManagerIdAsync(request.CompanyId, request.EmployeeId, cancellationToken);

        var generatedTaskIds = new List<Guid>();

        await CreateAssetReturnTasksAsync(request, plan, now, generatedTaskIds, cancellationToken);
        await CreateDocumentReviewTaskAsync(request, plan, now, generatedTaskIds, cancellationToken);
        await CreateManagerExitChecklistAsync(request, plan, employeeName, managerId, now, generatedTaskIds, cancellationToken);

        // OFF-03: the OffboardingPlan and every OffboardingTask are made durable in one transaction
        // BEFORE any cross-module call to the Tasks module — a general (Tasks-module) task must
        // never be created before its OffboardingTask source row exists. If two requests race to
        // start offboarding for the same employee concurrently, the unique partial index on
        // (company_id, employee_id) rejects the second insert here and we surface that as the same
        // Conflict the pre-check above would have returned, rather than propagating a raw DB
        // exception or creating two plans.
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Failure<StartOffboardingResponse>(
                Error.Conflict("An offboarding plan already exists for this employee."));
        }

        // Now that the plan/tasks are durable, synchronise the corresponding Tasks-module TaskItems.
        // A failure here is isolated per task (see OffboardingTaskSynchronizer) and is not a
        // log-only dead end: any task left unsynced is retried by
        // OffboardingPlanCreationReconciliationJob until it succeeds.
        await taskSynchronizer.SyncPlanAsync(plan.CompanyId, plan.Id, cancellationToken);

        await NotifyOffboardingStartedAsync(plan, employeeName, managerId, now, cancellationToken);

        await integrationEventPublisher.PublishAsync(
            new OffboardingStartedIntegrationEvent(plan.CompanyId, plan.EmployeeId, now),
            cancellationToken);

        return Result.Success(new StartOffboardingResponse(
            plan.Id,
            plan.CompanyId,
            plan.EmployeeId,
            plan.LastWorkingDay,
            plan.Status.ToString(),
            plan.Notes,
            generatedTaskIds,
            plan.CreatedAt));
    }

    private async Task CreateAssetReturnTasksAsync(
        StartOffboardingRequest request,
        OffboardingPlan plan,
        DateTimeOffset now,
        List<Guid> generatedTaskIds,
        CancellationToken cancellationToken)
    {
        var assignedAssets = await assignedAssetReader.GetAssignedAssetsAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        foreach (var asset in assignedAssets)
        {
            var title = $"Return asset: {asset.AssetLabel}";

            var task = OffboardingTask.Create(
                Guid.NewGuid(), request.CompanyId, plan.Id,
                title, description: null,
                OffboardingTaskAssignTo.Employee,
                dueDate: request.LastWorkingDay, now: now, assignedEmployeeId: request.EmployeeId,
                assetAssignmentId: asset.AssetAssignmentId);
            dbContext.OffboardingTasks.Add(task);
            generatedTaskIds.Add(task.Id);
        }
    }

    private async Task CreateDocumentReviewTaskAsync(
        StartOffboardingRequest request,
        OffboardingPlan plan,
        DateTimeOffset now,
        List<Guid> generatedTaskIds,
        CancellationToken cancellationToken)
    {
        var outstandingRequests = await documentReader.GetOutstandingRequestsAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        var description = outstandingRequests.Count == 0
            ? "No outstanding document requests."
            : $"{outstandingRequests.Count} outstanding document request(s) to resolve before exit.";

        const string title = "Review outstanding documents for employee exit";

        var task = OffboardingTask.Create(
            Guid.NewGuid(), request.CompanyId, plan.Id,
            title, description,
            OffboardingTaskAssignTo.HR,
            dueDate: request.LastWorkingDay, now: now, assignedEmployeeId: null);
        dbContext.OffboardingTasks.Add(task);
        generatedTaskIds.Add(task.Id);
    }

    private Task CreateManagerExitChecklistAsync(
        StartOffboardingRequest request,
        OffboardingPlan plan,
        string employeeName,
        Guid? managerId,
        DateTimeOffset now,
        List<Guid> generatedTaskIds,
        CancellationToken cancellationToken)
    {
        string[] checklistTitles =
        [
            $"Conduct exit interview — {employeeName}",
            $"Revoke system access and accounts — {employeeName}",
            $"Arrange handover and knowledge transfer — {employeeName}",
            $"Notify IT and Payroll of employee exit — {employeeName}",
        ];

        foreach (var title in checklistTitles)
        {
            var task = OffboardingTask.Create(
                Guid.NewGuid(), request.CompanyId, plan.Id,
                title, description: null,
                OffboardingTaskAssignTo.Manager,
                dueDate: request.LastWorkingDay, now: now, assignedEmployeeId: managerId);
            dbContext.OffboardingTasks.Add(task);
            generatedTaskIds.Add(task.Id);
        }

        return Task.CompletedTask;
    }

    private async Task NotifyOffboardingStartedAsync(
        OffboardingPlan plan,
        string employeeName,
        Guid? managerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (managerId.HasValue)
        {
            await notificationWriter.WriteAsync(
                Guid.NewGuid(), plan.CompanyId, managerId.Value,
                $"Offboarding started for {employeeName}",
                $"{employeeName}'s offboarding plan has been created with their exit tasks. Review their checklist.",
                plan.Id,
                NotificationType.OffboardingStarted,
                NotificationPriority.Normal,
                now,
                cancellationToken);
        }

        await notificationWriter.WriteAsync(
            Guid.NewGuid(), plan.CompanyId, plan.EmployeeId,
            "Your offboarding has started",
            "Your offboarding checklist has been created — check your tasks before your last working day.",
            plan.Id,
            NotificationType.OffboardingStarted,
            NotificationPriority.Normal,
            now,
            cancellationToken);
    }
}
