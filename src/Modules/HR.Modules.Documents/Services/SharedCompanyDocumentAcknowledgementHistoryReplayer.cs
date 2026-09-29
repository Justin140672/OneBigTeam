using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

internal sealed class SharedCompanyDocumentAcknowledgementHistoryReplayer(
    DocumentsDbContext dbContext,
    IIntegrationEventPublisher integrationEventPublisher) : ISharedCompanyDocumentAcknowledgementHistoryReplayer
{
    public async Task<int> ReplaySharedCompanyDocumentAcknowledgedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var acknowledgements = await dbContext.SharedCompanyDocumentAcknowledgements
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId)
            .Join(
                dbContext.SharedCompanyDocuments.AsNoTracking(),
                a => a.SharedCompanyDocumentId,
                d => d.Id,
                (a, d) => new { a.CompanyId, a.EmployeeId, a.SharedCompanyDocumentId, d.Title, a.AcknowledgedAt })
            .ToListAsync(cancellationToken);

        foreach (var acknowledgement in acknowledgements)
        {
            await integrationEventPublisher.PublishAsync(
                new SharedCompanyDocumentAcknowledgedIntegrationEvent(
                    acknowledgement.CompanyId,
                    acknowledgement.EmployeeId,
                    acknowledgement.SharedCompanyDocumentId,
                    acknowledgement.Title,
                    acknowledgement.AcknowledgedAt),
                cancellationToken);
        }

        return acknowledgements.Count;
    }
}
