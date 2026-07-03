using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.ReturnToWorkReviewRequired;

internal sealed class ReturnToWorkReviewRequiredHandler(
    ITaskCreator taskCreator,
    IEmployeeNameReader employeeNameReader,
    IManagerReader managerReader) : IIntegrationEventHandler<ReturnToWorkReviewRequiredIntegrationEvent>
{
    private static readonly Guid SystemUserId = Guid.Empty;

    public async Task HandleAsync(ReturnToWorkReviewRequiredIntegrationEvent e, CancellationToken cancellationToken)
    {
        var names = await employeeNameReader.GetNamesAsync(e.CompanyId, [e.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(e.EmployeeId, "Unknown Employee");

        var managerId = await managerReader.GetManagerIdAsync(e.CompanyId, e.EmployeeId, cancellationToken);

        await taskCreator.CreateAsync(
            e.CompanyId,
            createdBy:          SystemUserId,
            title:              $"Return-to-work review — {employeeName}",
            description:        $"{employeeName} has returned to work following a period of sickness absence. Conduct a return-to-work review.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Sickness,
            actionType:         TaskActionType.Review,
            dueDate:            e.DueDate,
            assignedEmployeeId: managerId,
            assignedUserId:     managerId,
            sourceEntityId:     e.ReviewId,
            cancellationToken);
    }
}
