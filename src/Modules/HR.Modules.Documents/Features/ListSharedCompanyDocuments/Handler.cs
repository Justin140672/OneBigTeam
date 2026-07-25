using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ListSharedCompanyDocuments;

internal sealed class ListSharedCompanyDocumentsHandler(DocumentsDbContext db, IEmployeeNameReader employeeNameReader)
{
    public async Task<Result<ListSharedCompanyDocumentsResponse>> HandleAsync(
        ListSharedCompanyDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.SharedCompanyDocuments
            .AsNoTracking()
            .Where(d => d.CompanyId == request.CompanyId);

        var documents = await query
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        var categoryIds = documents.Select(d => d.CategoryId).ToHashSet();
        var categoryNames = categoryIds.Count > 0
            ? await db.CompanyDocumentCategories
                .AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var idsToResolve = documents.Select(d => d.UpdatedBy).Distinct().ToList();
        idsToResolve.AddRange(documents
            .Where(d => d.ReviewOwnerEmployeeId is not null)
            .Select(d => d.ReviewOwnerEmployeeId!.Value)
            .Distinct());
        var namesLookup = await employeeNameReader.GetNamesAsync(request.CompanyId, idsToResolve.Distinct(), cancellationToken);

        var items = documents
            .Select(d => new SharedCompanyDocumentListItem(
                d.Id,
                d.Title,
                d.Description,
                d.CategoryId,
                categoryNames.TryGetValue(d.CategoryId, out var name) ? name : "Unknown",
                d.FileName,
                d.VersionNumber,
                d.Status.ToString(),
                d.EffectiveDate,
                d.ReviewDate,
                d.ReviewFrequency.ToString(),
                d.ReviewOwnerEmployeeId,
                d.ReviewOwnerEmployeeId is { } reviewOwnerEmployeeId
                    ? (namesLookup.TryGetValue(reviewOwnerEmployeeId, out var reviewOwnerName) ? reviewOwnerName : "Unknown")
                    : null,
                d.CreatedAt,
                d.UpdatedAt,
                namesLookup.TryGetValue(d.UpdatedBy, out var updatedByName) ? updatedByName : "Unknown"))
            .ToList();

        return Result.Success(new ListSharedCompanyDocumentsResponse(items));
    }
}
