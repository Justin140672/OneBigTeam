using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
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
        var (myDepartmentId, myLocationId) = await audienceReader.GetEmployeeAudienceAsync(
            request.CompanyId, callerEmployeeId, cancellationToken);

        var documents = await db.SharedCompanyDocuments
            .AsNoTracking()
            .Where(d => d.CompanyId == request.CompanyId && d.Status == SharedCompanyDocumentStatus.Published)
            .Where(d =>
                (d.AudienceDepartmentId == null && d.AudienceLocationId == null) ||
                (d.AudienceDepartmentId != null && d.AudienceDepartmentId == myDepartmentId) ||
                (d.AudienceLocationId != null && d.AudienceLocationId == myLocationId))
            .OrderBy(d => d.Title)
            .ToListAsync(cancellationToken);

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
