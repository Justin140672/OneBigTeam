using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Infrastructure.Abstractions;
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
    ITaskCreator taskCreator,
    INotificationWriter notificationWriter)
{
    public async Task<Result<StartOffboardingResponse>> HandleAsync(
        StartOffboardingRequest request,
        CancellationToken cancellationToken)
    {
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

        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), request.CompanyId, request.EmployeeId, request.LastWorkingDay, request.Notes, now);
        dbContext.OffboardingPlans.Add(plan);
        plan.Start(now);

        var managerId = await managerReader.GetManagerIdAsync(request.CompanyId, request.EmployeeId, cancellationToken);

        var generatedTaskIds = new List<Guid>();

        await CreateAssetReturnTasksAsync(request, plan, employeeName, now, generatedTaskIds, cancellationToken);
        await CreateDocumentReviewTaskAsync(request, plan, now, generatedTaskIds, cancellationToken);
        await CreateManagerExitChecklistAsync(request, plan, employeeName, managerId, now, generatedTaskIds, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        await NotifyOffboardingStartedAsync(plan, employeeName, managerId, now, cancellationToken);

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
        string employeeName,
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
                OffboardingTaskAssignTo.Employee, request.LastWorkingDay, now);
            dbContext.OffboardingTasks.Add(task);
            generatedTaskIds.Add(task.Id);

            await taskCreator.CreateAsync(
                request.CompanyId,
                createdBy:          request.EmployeeId,
                title:              title,
                description:        null,
                priority:           TaskPriority.Medium,
                source:             TaskSource.Offboarding,
                actionType:         TaskActionType.Complete,
                dueDate:            request.LastWorkingDay,
                assignedEmployeeId: request.EmployeeId,
                assignedUserId:     request.EmployeeId,
                sourceEntityId:     task.Id,
                cancellationToken);
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
            OffboardingTaskAssignTo.HR, request.LastWorkingDay, now);
        dbContext.OffboardingTasks.Add(task);
        generatedTaskIds.Add(task.Id);

        await taskCreator.CreateAsync(
            request.CompanyId,
            createdBy:          request.EmployeeId,
            title:              title,
            description:        description,
            priority:           TaskPriority.Medium,
            source:             TaskSource.Offboarding,
            actionType:         TaskActionType.Complete,
            dueDate:            request.LastWorkingDay,
            assignedEmployeeId: null,
            assignedUserId:     null,
            sourceEntityId:     task.Id,
            cancellationToken);
    }

    private async Task CreateManagerExitChecklistAsync(
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
                OffboardingTaskAssignTo.Manager, request.LastWorkingDay, now);
            dbContext.OffboardingTasks.Add(task);
            generatedTaskIds.Add(task.Id);

            await taskCreator.CreateAsync(
                request.CompanyId,
                createdBy:          request.EmployeeId,
                title:              title,
                description:        null,
                priority:           TaskPriority.Medium,
                source:             TaskSource.Offboarding,
                actionType:         TaskActionType.Complete,
                dueDate:            request.LastWorkingDay,
                assignedEmployeeId: managerId,
                assignedUserId:     managerId,
                sourceEntityId:     task.Id,
                cancellationToken);
        }
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
