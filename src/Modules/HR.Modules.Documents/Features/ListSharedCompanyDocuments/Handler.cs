using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.Modules.Employees.Contracts;
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

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(d => d.Title.ToLower().Contains(search));
        }

        if (request.CategoryId is not null)
            query = query.Where(d => d.CategoryId == request.CategoryId);

        if (request.Status is not null)
            query = query.Where(d => d.Status == request.Status);

        var totalCount = await query.CountAsync(cancellationToken);

        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize   = request.PageSize   < 1 ? 20 : request.PageSize;

        var documents = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
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

        var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);

        return Result.Success(new ListSharedCompanyDocumentsResponse(items, totalCount, pageNumber, pageSize, totalPages));
    }
}
