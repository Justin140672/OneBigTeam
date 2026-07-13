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

        if (request.Status is not null)
            query = query.Where(d => d.Status == request.Status);

        if (request.CategoryId is not null)
            query = query.Where(d => d.CategoryId == request.CategoryId);

        if (request.ReviewDateFrom is not null)
            query = query.Where(d => d.ReviewDate != null && d.ReviewDate >= request.ReviewDateFrom);

        if (request.ReviewDateTo is not null)
            query = query.Where(d => d.ReviewDate != null && d.ReviewDate <= request.ReviewDateTo);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(d => d.Title.ToLower().Contains(search));
        }

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

        var updatedByIds = documents.Select(d => d.UpdatedBy).Distinct().ToList();
        var updatedByNames = await employeeNameReader.GetNamesAsync(request.CompanyId, updatedByIds, cancellationToken);

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
                d.CreatedAt,
                d.UpdatedAt,
                updatedByNames.TryGetValue(d.UpdatedBy, out var updatedByName) ? updatedByName : "Unknown"))
            .ToList();

        return Result.Success(new ListSharedCompanyDocumentsResponse(items));
    }
}
