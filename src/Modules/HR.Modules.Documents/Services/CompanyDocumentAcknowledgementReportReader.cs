using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

internal sealed class CompanyDocumentAcknowledgementReportReader(
    DocumentsDbContext dbContext,
    SharedCompanyDocumentAudienceMatcher audienceMatcher) : ICompanyDocumentAcknowledgementReportReader
{
    public async Task<IReadOnlyList<CompanyDocumentAcknowledgementReportItem>> GetAcknowledgementReportAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var documents = await dbContext.SharedCompanyDocuments
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId
                     && d.Status == SharedCompanyDocumentStatus.Published
                     && d.RequiresAcknowledgement)
            .ToListAsync(cancellationToken);

        if (documents.Count == 0)
            return [];

        var results = new List<CompanyDocumentAcknowledgementReportItem>();

        foreach (var document in documents)
        {
            var eligibleIds = await audienceMatcher.GetEligibleEmployeeIdsAsync(companyId, document.Id, cancellationToken);
            if (eligibleIds.Count == 0)
                continue;

            var acknowledgementsByEmployeeId = await dbContext.SharedCompanyDocumentAcknowledgements
                .AsNoTracking()
                .Where(a => a.SharedCompanyDocumentId == document.Id && a.VersionNumber == document.VersionNumber)
                .ToDictionaryAsync(a => a.EmployeeId, a => a, cancellationToken);

            foreach (var employeeId in eligibleIds)
            {
                acknowledgementsByEmployeeId.TryGetValue(employeeId, out var acknowledgement);

                results.Add(new CompanyDocumentAcknowledgementReportItem(
                    document.Id,
                    document.Title,
                    employeeId,
                    acknowledgement is not null,
                    acknowledgement?.AcknowledgedAt));
            }
        }

        return results;
    }
}
