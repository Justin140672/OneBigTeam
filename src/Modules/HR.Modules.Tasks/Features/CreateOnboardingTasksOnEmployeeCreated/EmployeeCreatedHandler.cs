using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.CreateOnboardingTasksOnEmployeeCreated;

internal sealed class EmployeeCreatedHandler(
    ITaskCreator taskCreator,
    IEmployeeNameReader employeeNameReader) : IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeCreatedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var names = await employeeNameReader.GetNamesAsync(e.CompanyId, [e.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(e.EmployeeId, "the new employee");

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
}
