using HR.Modules.Tasks.Contracts;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
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
    IIntegrationEventPublisher integrationEventPublisher,
    ICompanyLeavingSettingsReader leavingSettingsReader,
    IHrAdministratorDirectory hrAdministratorDirectory,
    IDirectReportsReader directReportsReader,
    ITaskReassigner taskReassigner,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<StartOffboardingResponse>> HandleAsync(
        StartOffboardingRequest request,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated "start offboarding" request before
        // doing any business work, so a repeated delivery can't attempt to create a second plan
        // (surfacing as a confusing Conflict on retry) once the first attempt already succeeded.
        // Note: unlike AdjustLeaveBalance, the idempotency record here is NOT saved in the same
        // SaveChangesAsync call as the plan/task creation below — this handler already has its own
        // unique-active-plan-index conflict detection (DbUpdateException catch further down), and
        // combining the two would make SaveIdempotentAsync's generic "any 23505 means a concurrent
        // duplicate of THIS key" handling misinterpret that unrelated constraint violation. Instead
        // the record is saved on its own, in isolation, right before the handler returns success —
        // this still lets a genuine retry (client resubmits after not receiving the first response)
        // short-circuit here instead of hitting the active-plan conflict check below.
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, StartOffboardingResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<StartOffboardingResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, [request.EmployeeId], cancellationToken);
        if (!names.TryGetValue(request.EmployeeId, out var employeeNameValue))
            return Result.Failure<StartOffboardingResponse>(Error.NotFound("Employee not found."));

        var employeeName = string.IsNullOrEmpty(employeeNameValue) ? "the employee" : employeeNameValue;

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

        var isBackdated = request.LastWorkingDay <= DateOnly.FromDateTime(now.UtcDateTime);

        var accessAlreadyDisabled = isBackdated
            && await leavingSettingsReader.GetAutoDisableAccessOnLeavingDateAsync(request.CompanyId, cancellationToken);

        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), request.CompanyId, request.EmployeeId, request.LastWorkingDay, request.Notes, now,
            isBackdated);
        dbContext.OffboardingPlans.Add(plan);
        plan.Start(now);

        var managerId = await managerReader.GetManagerIdAsync(request.CompanyId, request.EmployeeId, cancellationToken);

        var directReportIds = await directReportsReader.GetDirectReportIdsAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);
        var isDepartingManager = directReportIds.Count > 0;

        var needsManagerReassignmentEscalation = isDepartingManager && request.ReplacementManagerEmployeeId is null;

        Guid? hrReconciliationAssigneeId = null;
        if (isBackdated || needsManagerReassignmentEscalation)
        {
            var hrAdministratorIds = await hrAdministratorDirectory.GetHrAdministratorEmployeeIdsAsync(
                request.CompanyId, cancellationToken);
            hrReconciliationAssigneeId = hrAdministratorIds.Count == 0
                ? null
                : hrAdministratorIds.OrderBy(id => id).First();
        }

        var generatedTaskIds = new List<Guid>();

        await CreateAssetReturnTasksAsync(
            request, plan, isBackdated, hrReconciliationAssigneeId, now, generatedTaskIds, cancellationToken);
        await CreateDocumentReviewTaskAsync(
            request, plan, isBackdated, now, generatedTaskIds, cancellationToken);
        await CreateManagerExitChecklistAsync(
            request, plan, employeeName, managerId, isBackdated, accessAlreadyDisabled, now, generatedTaskIds,
            cancellationToken);

        if (needsManagerReassignmentEscalation)
        {
            await CreateManagerReassignmentExceptionTasksAsync(
                request, plan, directReportIds, hrReconciliationAssigneeId, now, generatedTaskIds, cancellationToken);
        }

        var reconciliationTaskCreated = dbContext.OffboardingTasks.Local
            .Any(t => t.OffboardingPlanId == plan.Id && t.RequiresHrConfirmation);

        if (reconciliationTaskCreated)
            plan.MarkHrReconciliationRequired(now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Failure<StartOffboardingResponse>(
                Error.Conflict("An offboarding plan already exists for this employee."));
        }

        await taskSynchronizer.SyncPlanAsync(plan.CompanyId, plan.Id, cancellationToken);

        if (isDepartingManager)
        {
            await taskReassigner.ReassignAllByAssigneeAsync(
                request.CompanyId, request.EmployeeId, request.ReplacementManagerEmployeeId, cancellationToken);
        }

        await NotifyOffboardingStartedAsync(
            plan, employeeName, managerId, isBackdated, accessAlreadyDisabled, now, cancellationToken);

        await auditEventPublisher.PublishAsync(
            new OffboardingPlanStartedAuditEvent(
                plan.CompanyId,
                plan.Id,
                plan.EmployeeId,
                request.ActorEmployeeId ?? OffboardingSystemActor.Id,
                plan.LastWorkingDay,
                generatedTaskIds.Count,
                dbContext.OffboardingTasks.Local.Count(t => t.OffboardingPlanId == plan.Id && t.IsMandatory),
                isBackdated,
                now),
            cancellationToken);

        await integrationEventPublisher.PublishAsync(
            new OffboardingStartedIntegrationEvent(plan.CompanyId, plan.EmployeeId, now),
            cancellationToken);

        var response = new StartOffboardingResponse(
            plan.Id,
            plan.CompanyId,
            plan.EmployeeId,
            plan.LastWorkingDay,
            plan.Status.ToString(),
            plan.Notes,
            generatedTaskIds,
            plan.CreatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(
                dbContext.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status201Created, response, now,
                cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }

        return Result.Success(response);
    }

    private async Task CreateAssetReturnTasksAsync(
        StartOffboardingRequest request,
        OffboardingPlan plan,
        bool isBackdated,
        Guid? hrReconciliationAssigneeId,
        DateTimeOffset now,
        List<Guid> generatedTaskIds,
        CancellationToken cancellationToken)
    {
        var assignedAssets = await assignedAssetReader.GetAssignedAssetsAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        foreach (var asset in assignedAssets)
        {
            OffboardingTask task;

            if (isBackdated)
            {
                var title = $"Confirm return of asset: {asset.AssetLabel} (backdated departure — reconciliation required)";
                var description = "Employee's departure was backdated; this asset return must be " +
                    "confirmed and reconciled by HR rather than actioned by the former employee.";

                task = OffboardingTask.Create(
                    Guid.NewGuid(), request.CompanyId, plan.Id,
                    title, description,
                    OffboardingTaskAssignTo.HR,
                    dueDate: request.LastWorkingDay, now: now, assignedEmployeeId: hrReconciliationAssigneeId,
                    assetAssignmentId: asset.AssetAssignmentId,
                    requiresHrConfirmation: true);
            }
            else
            {
                var title = $"Return asset: {asset.AssetLabel}";

                task = OffboardingTask.Create(
                    Guid.NewGuid(), request.CompanyId, plan.Id,
                    title, description: null,
                    OffboardingTaskAssignTo.Employee,
                    dueDate: request.LastWorkingDay, now: now, assignedEmployeeId: request.EmployeeId,
                    assetAssignmentId: asset.AssetAssignmentId);
            }

            dbContext.OffboardingTasks.Add(task);
            generatedTaskIds.Add(task.Id);
        }
    }

    private async Task CreateDocumentReviewTaskAsync(
        StartOffboardingRequest request,
        OffboardingPlan plan,
        bool isBackdated,
        DateTimeOffset now,
        List<Guid> generatedTaskIds,
        CancellationToken cancellationToken)
    {
        var outstandingRequests = await documentReader.GetOutstandingRequestsAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        var isReconciliation = isBackdated && outstandingRequests.Count > 0;

        var description = outstandingRequests.Count == 0
            ? "No outstanding document requests."
            : isReconciliation
                ? $"{outstandingRequests.Count} outstanding document request(s) to resolve before exit. " +
                    "Employee's departure was backdated — confirm and reconcile these directly with HR " +
                    "rather than waiting on the former employee."
                : $"{outstandingRequests.Count} outstanding document request(s) to resolve before exit.";

        const string title = "Review outstanding documents for employee exit";

        var task = OffboardingTask.Create(
            Guid.NewGuid(), request.CompanyId, plan.Id,
            title, description,
            OffboardingTaskAssignTo.HR,
            dueDate: request.LastWorkingDay, now: now, assignedEmployeeId: null,
            requiresHrConfirmation: isReconciliation);
        dbContext.OffboardingTasks.Add(task);
        generatedTaskIds.Add(task.Id);
    }

    private async Task CreateManagerReassignmentExceptionTasksAsync(
        StartOffboardingRequest request,
        OffboardingPlan plan,
        IReadOnlyList<Guid> directReportIds,
        Guid? hrReconciliationAssigneeId,
        DateTimeOffset now,
        List<Guid> generatedTaskIds,
        CancellationToken cancellationToken)
    {
        var reportNames = await employeeNameReader.GetNamesAsync(request.CompanyId, directReportIds, cancellationToken);

        var existingTitles = dbContext.OffboardingTasks.Local
            .Where(t => t.OffboardingPlanId == plan.Id)
            .Select(t => t.Title)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var reportId in directReportIds)
        {
            var reportName = reportNames.GetValueOrDefault(reportId, "Unknown Employee");
            var title = $"Assign new manager for {reportName} (manager departing)";

            if (!existingTitles.Add(title))
                continue; // Already generated for this report in this run — avoid duplicates.

            var task = OffboardingTask.Create(
                Guid.NewGuid(), request.CompanyId, plan.Id,
                title,
                $"{reportName} reported to the departing employee and has no confirmed replacement " +
                    "manager. Assign a new manager and update any pending approvals/reviews accordingly.",
                OffboardingTaskAssignTo.HR,
                dueDate: request.LastWorkingDay, now: now, assignedEmployeeId: hrReconciliationAssigneeId,
                requiresHrConfirmation: true);

            dbContext.OffboardingTasks.Add(task);
            generatedTaskIds.Add(task.Id);
        }
    }

    private Task CreateManagerExitChecklistAsync(
        StartOffboardingRequest request,
        OffboardingPlan plan,
        string employeeName,
        Guid? managerId,
        bool isBackdated,
        bool accessAlreadyDisabled,
        DateTimeOffset now,
        List<Guid> generatedTaskIds,
        CancellationToken cancellationToken)
    {
        string[] checklistTitles =
        [
            $"Conduct exit interview — {employeeName}",
            $"Revoke system access and accounts — {employeeName}",
            $"Arrange handover and knowledge transfer — {employeeName}",
            $"Notify IT and Finance of employee exit — {employeeName}",
        ];

        foreach (var title in checklistTitles)
        {
            OffboardingTask task;

            var isMootAccessRevocation = isBackdated && accessAlreadyDisabled
                && title.StartsWith("Revoke system access", StringComparison.Ordinal);

            if (isMootAccessRevocation)
            {
                task = OffboardingTask.CreateWaived(
                    Guid.NewGuid(), request.CompanyId, plan.Id,
                    title,
                    "Waived automatically — employee's departure was backdated and system access was " +
                        "already disabled on confirmation of their leaving date.",
                    OffboardingTaskAssignTo.Manager,
                    dueDate: request.LastWorkingDay, now: now);
            }
            else
            {
                task = OffboardingTask.Create(
                    Guid.NewGuid(), request.CompanyId, plan.Id,
                    title, description: null,
                    OffboardingTaskAssignTo.Manager,
                    dueDate: request.LastWorkingDay, now: now, assignedEmployeeId: managerId);
            }

            dbContext.OffboardingTasks.Add(task);
            generatedTaskIds.Add(task.Id);
        }

        return Task.CompletedTask;
    }

    private async Task NotifyOffboardingStartedAsync(
        OffboardingPlan plan,
        string employeeName,
        Guid? managerId,
        bool isBackdated,
        bool accessAlreadyDisabled,
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

        var employeeNotificationIsUnusable = isBackdated && accessAlreadyDisabled;

        if (!employeeNotificationIsUnusable)
        {
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

        if (!plan.RequiresHrReconciliation)
            return;

        var hrAdministratorIds = await hrAdministratorDirectory.GetHrAdministratorEmployeeIdsAsync(
            plan.CompanyId, cancellationToken);

        foreach (var hrAdministratorId in hrAdministratorIds)
        {
            var alreadySent = await notificationWriter.ExistsAsync(
                hrAdministratorId, plan.Id, NotificationType.OffboardingRequiresHrReconciliation, cancellationToken);

            if (alreadySent)
                continue;

            await notificationWriter.WriteAsync(
                Guid.NewGuid(), plan.CompanyId, hrAdministratorId,
                $"Offboarding needs HR reconciliation — {employeeName}",
                $"{employeeName}'s departure was backdated. Outstanding assets, documents and/or access " +
                    "could not be routed to them and need HR confirmation.",
                plan.Id,
                NotificationType.OffboardingRequiresHrReconciliation,
                NotificationPriority.High,
                now,
                cancellationToken);
        }
    }
}
