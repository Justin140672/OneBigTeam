using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.CreateOnboardingTasksOnEmployeeCreated;

internal sealed class EmployeeCreatedHandler(
    ITaskCreator taskCreator,
    IEmployeeNameReader employeeNameReader,
    IOnboardingTemplateReader onboardingTemplateReader) : IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeCreatedIntegrationEvent e, CancellationToken cancellationToken)
    {
        if (e.IsImported)
            return;

        var names = await employeeNameReader.GetNamesAsync(e.CompanyId, [e.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(e.EmployeeId, "the new employee");

        var templateTasks = await GetTemplateTasksAsync(e, cancellationToken);

        if (templateTasks is not null)
        {
            foreach (var task in templateTasks)
            {
                var (assignedEmployeeId, assignedUserId) = task.AssignTo switch
                {
                    OnboardingTemplateTaskAssignTo.NewHire => (e.EmployeeId, (Guid?)e.EmployeeId),
                    OnboardingTemplateTaskAssignTo.Manager => (e.ManagerId, e.ManagerId),
                    _ => ((Guid?)null, (Guid?)null),
                };

                await taskCreator.CreateAsync(
                    e.CompanyId,
                    createdBy:          e.EmployeeId,
                    title:              $"{task.Title} — {employeeName}",
                    description:        task.Description,
                    priority:           task.Priority,
                    source:             TaskSource.Onboarding,
                    actionType:         TaskActionType.Complete,
                    dueDate:            e.StartDate.AddDays(task.DueDaysAfterStart),
                    assignedEmployeeId: assignedEmployeeId,
                    assignedUserId:     assignedUserId,
                    sourceEntityId:     null,
                    cancellationToken);
            }

            return;
        }

        await taskCreator.CreateAsync(
            e.CompanyId,
            createdBy:          e.EmployeeId,
            title:              $"Set up workstation and system access — {employeeName}",
            description:        $"Provision equipment, accounts and system access ahead of {employeeName}'s start date.",
            priority:           TaskPriority.High,
            source:             TaskSource.Onboarding,
            actionType:         TaskActionType.Complete,
            dueDate:            e.StartDate,
            assignedEmployeeId: null,
            assignedUserId:     null,
            sourceEntityId:     null,
            cancellationToken);

        await taskCreator.CreateAsync(
            e.CompanyId,
            createdBy:          e.EmployeeId,
            title:              $"Send welcome email and first-day details — {employeeName}",
            description:        $"Send {employeeName} their first-day joining instructions and welcome pack.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Onboarding,
            actionType:         TaskActionType.Complete,
            dueDate:            e.StartDate,
            assignedEmployeeId: e.ManagerId,
            assignedUserId:     e.ManagerId,
            sourceEntityId:     null,
            cancellationToken);

        await taskCreator.CreateAsync(
            e.CompanyId,
            createdBy:          e.EmployeeId,
            title:              $"Schedule welcome and induction meeting — {employeeName}",
            description:        $"Book an induction meeting with {employeeName} during their first week.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Onboarding,
            actionType:         TaskActionType.Complete,
            dueDate:            e.StartDate.AddDays(7),
            assignedEmployeeId: e.ManagerId,
            assignedUserId:     e.ManagerId,
            sourceEntityId:     null,
            cancellationToken);
    }

    /// <summary>
    /// Returns the active checklist tasks for the employee's onboarding template, or null if
    /// the employee has no position profile, the profile has no linked template, or the
    /// template currently has no active tasks — in every such case callers must fall back to
    /// the default hardcoded onboarding checklist to remain backward compatible.
    /// </summary>
    private async Task<IReadOnlyList<OnboardingTemplateTaskItem>?> GetTemplateTasksAsync(
        EmployeeCreatedIntegrationEvent e,
        CancellationToken cancellationToken)
    {
        if (e.PositionProfileId is null)
            return null;

        var onboardingTemplateId = await onboardingTemplateReader.GetOnboardingTemplateIdForPositionProfileAsync(
            e.CompanyId, e.PositionProfileId.Value, cancellationToken);

        if (onboardingTemplateId is null)
            return null;

        var tasks = await onboardingTemplateReader.GetActiveTasksAsync(
            e.CompanyId, onboardingTemplateId.Value, cancellationToken);

        return tasks.Count == 0 ? null : tasks;
    }
}
