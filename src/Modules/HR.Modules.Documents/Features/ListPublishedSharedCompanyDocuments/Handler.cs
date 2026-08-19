using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ListPublishedSharedCompanyDocuments;

internal sealed class ListPublishedSharedCompanyDocumentsHandler(
    DocumentsDbContext db,
    IEmployeeAudienceReader audienceReader,
    IClock clock)
{
    public async Task<Result<ListPublishedSharedCompanyDocumentsResponse>> HandleAsync(
        ListPublishedSharedCompanyDocumentsRequest request,
        Guid callerEmployeeId,
        CancellationToken cancellationToken)
    {
        var myProfile = await audienceReader.GetEmployeeAudienceAsync(request.CompanyId, callerEmployeeId, cancellationToken);

        var publishedDocuments = await db.SharedCompanyDocuments
            .AsNoTracking()
            .Where(d => d.CompanyId == request.CompanyId && d.Status == SharedCompanyDocumentStatus.Published)
            .ToListAsync(cancellationToken);

        var documentIds = publishedDocuments.Select(d => d.Id).ToList();
        var rulesByDocument = (await db.SharedCompanyDocumentAudienceRules
                .AsNoTracking()
                .Where(r => documentIds.Contains(r.SharedCompanyDocumentId))
                .ToListAsync(cancellationToken))
            .ToLookup(r => r.SharedCompanyDocumentId);

        var documents = publishedDocuments
            .Where(d => SharedCompanyDocumentAudienceMatcher.IsInAudience(rulesByDocument[d.Id], myProfile, callerEmployeeId))
            .OrderByDescending(d => d.PublishedAt)
            .ThenBy(d => d.Title)
            .ToList();

        var categoryIds = documents.Select(d => d.CategoryId).ToHashSet();
        var categoryNames = categoryIds.Count > 0
            ? await db.CompanyDocumentCategories
                .AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var myAcknowledgements = await db.SharedCompanyDocumentAcknowledgements
            .AsNoTracking()
            .Where(a => a.EmployeeId == callerEmployeeId && documentIds.Contains(a.SharedCompanyDocumentId))
            .ToListAsync(cancellationToken);

        // Only an acknowledgement of the document's CURRENT version counts — an acknowledgement
        // of a superseded version must not surface as "acknowledged" for the current one.
        var currentVersionByDocument = documents.ToDictionary(d => d.Id, d => d.VersionNumber);
        var myAcknowledgedAtByDocument = myAcknowledgements
            .Where(a => currentVersionByDocument.TryGetValue(a.SharedCompanyDocumentId, out var currentVersion)
                        && a.VersionNumber == currentVersion)
            .ToDictionary(a => a.SharedCompanyDocumentId, a => (DateTimeOffset?)a.AcknowledgedAt);

        var today = DateOnly.FromDateTime(clock.UtcNow);

        var items = documents
            .Select(d =>
            {
                var ackAt = myAcknowledgedAtByDocument.TryGetValue(d.Id, out var a) ? a : null;
                var status = SharedCompanyDocumentAcknowledgementStatusCalculator.Calculate(
                    d.RequiresAcknowledgement, ackAt, d.AcknowledgementDueDate, today);

                return new PublishedSharedCompanyDocumentItem(
                    d.Id,
                    d.Title,
                    d.Description,
                    categoryNames.TryGetValue(d.CategoryId, out var name) ? name : "Unknown",
                    d.EffectiveDate,
                    d.RequiresAcknowledgement,
                    d.AcknowledgementDueDate,
                    ackAt,
                    d.PublishedAt,
                    status);
            })
            .ToList();

        return Result.Success(new ListPublishedSharedCompanyDocumentsResponse(items));
    }
}
