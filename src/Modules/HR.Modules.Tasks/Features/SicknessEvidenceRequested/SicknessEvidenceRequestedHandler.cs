using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.SicknessEvidenceRequested;

internal sealed class SicknessEvidenceRequestedHandler(
    ITaskCreator taskCreator) : IIntegrationEventHandler<SicknessEvidenceRequestedIntegrationEvent>
{
    private static readonly Guid SystemUserId = Guid.Empty;

    public async Task HandleAsync(SicknessEvidenceRequestedIntegrationEvent e, CancellationToken cancellationToken)
    {
        await taskCreator.CreateAsync(
            e.CompanyId,
            createdBy:          SystemUserId,
            title:              "Upload fit note",
            description:        "A fit note is required for your sickness absence. Please upload your fit note document.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Sickness,
            actionType:         TaskActionType.Upload,
            dueDate:            e.DueDate,
            assignedEmployeeId: e.EmployeeId,
            assignedUserId:     null,
            sourceEntityId:     e.EvidenceRequestId,
            cancellationToken);
    }
}
