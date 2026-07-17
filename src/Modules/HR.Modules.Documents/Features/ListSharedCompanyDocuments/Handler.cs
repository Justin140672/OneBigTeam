using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ListSharedCompanyDocuments;

internal sealed class ListSharedCompanyDocumentsHandler(DocumentsDbContext db, IEmployeeNameReader employeeNameReader, IClock clock)
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

        if (request.ReviewStatusFilter is not null)
        {
            // Mirrors the Overdue/due-within-7-days bucketing in
            // ListSharedCompanyDocumentsDueForReviewHandler (dashboard widget) — Expired is its
            // own bucket keyed off Status rather than ReviewDate, since an expired document may
            // still carry a stale ReviewDate.
            var today = DateOnly.FromDateTime(clock.UtcNow);
            var dueBy = today.AddDays(7);

            query = request.ReviewStatusFilter switch
            {
                SharedCompanyDocumentReviewStatusFilter.DueSoon => query.Where(d =>
                    d.Status != SharedCompanyDocumentStatus.Archived
                    && d.Status != SharedCompanyDocumentStatus.Expired
                    && d.ReviewDate != null
                    && d.ReviewDate >= today
                    && d.ReviewDate <= dueBy),
                SharedCompanyDocumentReviewStatusFilter.Overdue => query.Where(d =>
                    d.Status != SharedCompanyDocumentStatus.Archived
                    && d.Status != SharedCompanyDocumentStatus.Expired
                    && d.ReviewDate != null
                    && d.ReviewDate < today),
                SharedCompanyDocumentReviewStatusFilter.NoReview => query.Where(d =>
                    d.Status != SharedCompanyDocumentStatus.Archived
                    && d.Status != SharedCompanyDocumentStatus.Expired
                    && d.ReviewDate == null),
                SharedCompanyDocumentReviewStatusFilter.Expired => query.Where(d =>
                    d.Status == SharedCompanyDocumentStatus.Expired),
                _ => query,
            };
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
