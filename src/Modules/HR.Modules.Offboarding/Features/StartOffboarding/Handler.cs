using HR.Modules.Tasks.Contracts;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
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
    IIntegrationEventPublisher integrationEventPublisher,
    ICompanyLeavingSettingsReader leavingSettingsReader,
    IHrAdministratorDirectory hrAdministratorDirectory)
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

        // OFF-05: a "backdated" departure is one whose LastWorkingDay is already on or before
        // today when the plan is created — i.e. offboarding is being started retroactively for
        // someone who has (or imminently will have) already left, rather than being planned ahead
        // of their departure. Compared against UTC "today" here (matching the rest of this handler,
        // which is not company-timezone-aware elsewhere either) — a same-day start is treated as
        // backdated too, since access may already have been removed by
        // EmployeeDepartureFinalizer's immediate-confirmation path for a same-day leaving date.
        var isBackdated = request.LastWorkingDay <= DateOnly.FromDateTime(now.UtcDateTime);

        // OFF-05: whether the company's settings mean this employee's system access is already (or
        // will imminently be, with no further action) disabled — this is what determines whether the
        // "your offboarding has started" employee notification would be unusable, and whether the
        // "revoke system access" checklist item is now redundant. Only queried when it can actually
        // change generation behaviour (backdated case) — avoids an extra cross-module call on every
        // ordinary, forward-looking offboarding start.
        var accessAlreadyDisabled = isBackdated
            && await leavingSettingsReader.GetAutoDisableAccessOnLeavingDateAsync(request.CompanyId, cancellationToken);

        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), request.CompanyId, request.EmployeeId, request.LastWorkingDay, request.Notes, now,
            isBackdated);
        dbContext.OffboardingPlans.Add(plan);
        plan.Start(now);

        var managerId = await managerReader.GetManagerIdAsync(request.CompanyId, request.EmployeeId, cancellationToken);

        // OFF-05: resolved once, up front, and reused for every reconciliation task generated below —
        // mirrors ProbationReviewAssignment's "single deterministic HR assignee" approach for the
        // Tasks module's single-assignee model. Only resolved when actually needed (backdated).
        Guid? hrReconciliationAssigneeId = null;
        if (isBackdated)
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

        var reconciliationTaskCreated = dbContext.OffboardingTasks.Local
            .Any(t => t.OffboardingPlanId == plan.Id && t.RequiresHrConfirmation);

        if (reconciliationTaskCreated)
            plan.MarkHrReconciliationRequired(now);

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

        await NotifyOffboardingStartedAsync(
            plan, employeeName, managerId, isBackdated, accessAlreadyDisabled, now, cancellationToken);

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
                // OFF-05: an asset-return task is normally the employee's own self-service action
                // (they physically return the item). For a backdated departure the employee has
                // already left — and may already have no system access — so this cannot be left as
                // an employee-owned task waiting for a login that may never come. It is rerouted to
                // HR as an explicit reconciliation task: HR must confirm/chase the real-world
                // return, not the (possibly absent) former employee.
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

        // OFF-05: this task is already HR-assigned regardless of backdating — what changes for a
        // backdated departure with outstanding requests is that it becomes an explicit
        // reconciliation item (RequiresHrConfirmation), since the departed employee cannot supply
        // the documents themselves and HR must confirm how each outstanding request is resolved.
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
            $"Notify IT and Payroll of employee exit — {employeeName}",
        ];

        foreach (var title in checklistTitles)
        {
            OffboardingTask task;

            // OFF-05: "revoke system access" is a future-facing checklist item that becomes moot the
            // moment access has already been disabled synchronously by EmployeeDepartureFinalizer
            // for a backdated departure — creating it as live, actionable work would just duplicate
            // something already done. Waived explicitly (Skipped, with a reason) rather than silently
            // omitted, so it still shows up in the checklist as accounted-for.
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

        // OFF-05: suppress the employee-facing "your offboarding has started" notification when the
        // departure is backdated and the employee's system access is already (or imminently) removed
        // — sending them a prompt to review tasks they cannot act on (no login access) would be an
        // unusable notification, not a helpful one. The plan/task rows themselves are never skipped —
        // only this specific employee-facing notification is gated.
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

        // OFF-05: explicit, prominent HR alert for a backdated departure that produced outstanding
        // reconciliation work — distinct from the routine OffboardingStarted notice above. Fanned out
        // to every HR administrator (not just the single deterministic assignee the reconciliation
        // tasks themselves use), mirroring ProbationReviewAssignment.ResolveNotificationRecipients.
        // Guarded with ExistsAsync as a defence-in-depth idempotency check: StartOffboardingHandler
        // itself only runs once per plan (the unique active-plan index prevents a second plan/task
        // set ever being generated for the same employee), so this is a belt-and-braces check against
        // any future caller that might re-invoke notification logic for the same plan, not evidence
        // that duplication is otherwise possible today.
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
