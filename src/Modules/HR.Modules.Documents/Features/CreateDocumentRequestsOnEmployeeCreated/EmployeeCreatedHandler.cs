using HR.Modules.Tasks.Contracts;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.CreateDocumentRequestsOnEmployeeCreated;

internal sealed class EmployeeCreatedHandler(
    DocumentsDbContext dbContext,
    IPositionProfileDocumentsReader positionProfileDocumentsReader,
    IDocumentTypeReader documentTypeReader,
    ITaskCreator taskCreator,
    IClock clock) : IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeCreatedIntegrationEvent e, CancellationToken cancellationToken)
    {
        if (e.IsImported)
            return;

        if (e.PositionProfileId is null)
            return;

        var required = await positionProfileDocumentsReader.GetActiveDocumentsAsync(
            e.CompanyId, e.PositionProfileId.Value, cancellationToken);

        if (required.Count == 0)
            return;

        var existingTypeIds = await dbContext.DocumentRequests
            .Where(r => r.EmployeeId == e.EmployeeId)
            .Select(r => r.DocumentTypeId)
            .ToHashSetAsync(cancellationToken);

        var now = clock.UtcNowOffset();

        var toCreate = required
            .Where(doc => !existingTypeIds.Contains(doc.DocumentTypeId))
            .Select(doc => DocumentRequest.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                doc.DocumentTypeId,
                positionProfileRequiredDocumentId: doc.Id,
                dueDate: doc.DueDaysAfterStart.HasValue
                    ? e.StartDate.AddDays(doc.DueDaysAfterStart.Value)
                    : null,
                isMandatory: true,
                notes: null,
                requestedByEmployeeId: null,
                now))
            .ToList();

        if (toCreate.Count == 0)
            return;

        dbContext.DocumentRequests.AddRange(toCreate);
        await dbContext.SaveChangesAsync(cancellationToken);

        var typeNames = await documentTypeReader.GetNamesAsync(
            e.CompanyId,
            toCreate.Select(r => r.DocumentTypeId),
            cancellationToken);

        foreach (var request in toCreate)
        {
            var typeName = typeNames.GetValueOrDefault(request.DocumentTypeId, "Document");
            await taskCreator.CreateAsync(
                e.CompanyId,
                createdBy:           e.EmployeeId,
                title:               $"Upload {typeName}",
                description:         $"Please upload a copy of your {typeName}.",
                priority:            TaskPriority.Medium,
                source:              TaskSource.Document,
                actionType:          TaskActionType.Upload,
                dueDate:             request.DueDate,
                assignedEmployeeId:  e.EmployeeId,
                assignedUserId:      null,
                sourceEntityId:      request.Id,
                cancellationToken);
        }
    }
}
