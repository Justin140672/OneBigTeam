using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

internal sealed class CompanyDocumentAcknowledgementReportReader(
    DocumentsDbContext dbContext,
    SharedCompanyDocumentAudienceMatcher audienceMatcher) : ICompanyDocumentAcknowledgementReportReader
{
    // Row cap (OBT-720 perf pass) — see HR.Modules.Sickness.Services.SicknessReportReader.MaxRows
    // for rationale. This report's row count is document-count x eligible-employee-count, which
    // can't be bounded with a single Take at the DB query level (the eligible-employee set for
    // each document comes from SharedCompanyDocumentAudienceMatcher, not this reader's own query).
    // A final in-memory cap is applied instead as a defense-in-depth safety bound; the realistic
    // ceiling here (published/ack-required documents x company headcount) is far below this in
    // virtually every tenant, so this is not expected to trigger in practice.
    private const int MaxRows = 50_000;

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
            if (results.Count >= MaxRows)
                break;

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

        return results.Count > MaxRows ? results.Take(MaxRows).ToList() : results;
    }
}
