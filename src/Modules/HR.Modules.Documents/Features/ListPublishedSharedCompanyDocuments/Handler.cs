using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ListPublishedSharedCompanyDocuments;

internal sealed class ListPublishedSharedCompanyDocumentsHandler(
    DocumentsDbContext db,
    IEmployeeAudienceReader audienceReader)
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
            .OrderBy(d => d.Title)
            .ToList();

        var categoryIds = documents.Select(d => d.CategoryId).ToHashSet();
        var categoryNames = categoryIds.Count > 0
            ? await db.CompanyDocumentCategories
                .AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var items = documents
            .Select(d => new PublishedSharedCompanyDocumentItem(
                d.Id,
                d.Title,
                d.Description,
                categoryNames.TryGetValue(d.CategoryId, out var name) ? name : "Unknown",
                d.EffectiveDate))
            .ToList();

        return Result.Success(new ListPublishedSharedCompanyDocumentsResponse(items));
    }
}
