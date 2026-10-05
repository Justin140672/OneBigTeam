using HR.Modules.Tasks.Contracts;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Onboarding.Features.CreateOnboardingPlanOnEmployeeCreated;

internal sealed class EmployeeCreatedHandler(
    OnboardingDbContext dbContext,
    ITaskCreator taskCreator,
    IHrTaskCreator hrTaskCreator,
    IEmployeeNameReader employeeNameReader,
    IOnboardingTemplateReader onboardingTemplateReader,
    IClock clock,
    ILogger<EmployeeCreatedHandler> logger) : IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeCreatedIntegrationEvent e, CancellationToken cancellationToken)
    {
        if (e.IsImported || e.IsInitialCompanyAdmin)
            return;

        var now = clock.UtcNowOffset();

        var names = await employeeNameReader.GetNamesAsync(e.CompanyId, [e.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(e.EmployeeId, "the new employee");

        var plan = OnboardingPlan.Create(Guid.NewGuid(), e.CompanyId, e.EmployeeId, e.StartDate, notes: null, now);
        dbContext.OnboardingPlans.Add(plan);

        var templateTasks = await GetTemplateTasksAsync(e, cancellationToken);

        if (templateTasks.Count == 0)
        {
            logger.LogWarning(
                "No usable onboarding template for company {CompanyId}; created an empty onboarding plan {PlanId} for employee {EmployeeId}",
                e.CompanyId, plan.Id, e.EmployeeId);
        }

        foreach (var task in templateTasks)
        {
            var title = $"{task.Title} — {employeeName}";
            var dueDate = e.StartDate.AddDays(task.DueDaysAfterStart);

            var onboardingTask = OnboardingTask.Create(
                Guid.NewGuid(), e.CompanyId, plan.Id,
                title, task.Description,
                task.AssignTo, dueDate, now);
            dbContext.OnboardingTasks.Add(onboardingTask);

            if (task.AssignTo == OnboardingTemplateTaskAssignTo.Hr)
            {
                await hrTaskCreator.CreateForHrAsync(
                    e.CompanyId,
                    createdBy:      e.EmployeeId,
                    title:          title,
                    description:    task.Description,
                    priority:       task.Priority,
                    source:         TaskSource.Onboarding,
                    actionType:     TaskActionType.Complete,
                    dueDate:        dueDate,
                    sourceEntityId: onboardingTask.Id,
                    cancellationToken);
                continue;
            }

            var (assignedEmployeeId, assignedUserId) = task.AssignTo switch
            {
                OnboardingTemplateTaskAssignTo.NewHire => (e.EmployeeId, (Guid?)e.EmployeeId),
                OnboardingTemplateTaskAssignTo.Manager => (e.ManagerId, e.ManagerId),
                _ => ((Guid?)null, (Guid?)null),
            };

            await taskCreator.CreateAsync(
                e.CompanyId,
                createdBy:          e.EmployeeId,
                title:              title,
                description:        task.Description,
                priority:           task.Priority,
                source:             TaskSource.Onboarding,
                actionType:         TaskActionType.Complete,
                dueDate:            dueDate,
                assignedEmployeeId: assignedEmployeeId,
                assignedUserId:     assignedUserId,
                sourceEntityId:     onboardingTask.Id,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<OnboardingTemplateTaskItem>> GetTemplateTasksAsync(
        EmployeeCreatedIntegrationEvent e,
        CancellationToken cancellationToken)
    {
        if (e.PositionProfileId is { } positionProfileId)
        {
            var explicitTemplateId = await onboardingTemplateReader.GetOnboardingTemplateIdForPositionProfileAsync(
                e.CompanyId, positionProfileId, cancellationToken);

            if (explicitTemplateId is { } explicitId)
            {
                var explicitTasks = await onboardingTemplateReader.GetActiveTasksAsync(
                    e.CompanyId, explicitId, cancellationToken);

                if (explicitTasks.Count > 0)
                    return explicitTasks;
            }
        }

        var defaultTemplateId = await onboardingTemplateReader.GetDefaultOnboardingTemplateIdAsync(
            e.CompanyId, cancellationToken);

        if (defaultTemplateId is not { } defaultId)
            return [];

        return await onboardingTemplateReader.GetActiveTasksAsync(
            e.CompanyId, defaultId, cancellationToken);
    }
}
