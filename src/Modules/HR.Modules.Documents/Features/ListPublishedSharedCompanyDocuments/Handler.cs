using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ListPublishedSharedCompanyDocuments;

// NOTE: there is currently no per-document audience/assignment concept in this codebase —
// SharedCompanyDocument has no "who can see this" targeting beyond its Status. So "documents
// [the employee is] allowed to access" is implemented here as "every Published document in the
// company" — the broadest safe reading given the current schema. If per-document audience
// targeting (e.g. by department/location) is wanted later, this handler is where it would filter
// further.
internal sealed class ListPublishedSharedCompanyDocumentsHandler(DocumentsDbContext db)
{
    public async Task<Result<ListPublishedSharedCompanyDocumentsResponse>> HandleAsync(
        ListPublishedSharedCompanyDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var documents = await db.SharedCompanyDocuments
            .AsNoTracking()
            .Where(d => d.CompanyId == request.CompanyId && d.Status == SharedCompanyDocumentStatus.Published)
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
