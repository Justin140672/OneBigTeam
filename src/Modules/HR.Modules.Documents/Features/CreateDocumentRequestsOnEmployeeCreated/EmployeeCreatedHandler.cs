using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.CreateDocumentRequestsOnEmployeeCreated;

internal sealed class EmployeeCreatedHandler(
    DocumentsDbContext dbContext,
    IPositionProfileDocumentsReader positionProfileDocumentsReader,
    IClock clock) : IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeCreatedIntegrationEvent e, CancellationToken cancellationToken)
    {
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
                requestedByEmployeeId: null,
                now))
            .ToList();

        if (toCreate.Count == 0)
            return;

        dbContext.DocumentRequests.AddRange(toCreate);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
