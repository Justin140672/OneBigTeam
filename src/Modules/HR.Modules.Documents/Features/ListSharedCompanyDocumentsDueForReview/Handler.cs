using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ListSharedCompanyDocumentsDueForReview;

internal sealed class ListSharedCompanyDocumentsDueForReviewHandler(
    DocumentsDbContext db,
    IClock clock,
    IEmployeeNameReader employeeNameReader)
{
    public async Task<Result<ListSharedCompanyDocumentsDueForReviewResponse>> HandleAsync(
        ListSharedCompanyDocumentsDueForReviewRequest request,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);

        // Window covers overdue reviews (ReviewDate < today) as well as reviews due this week
        // (ReviewDate between today and today + 7 days inclusive) — the two buckets surfaced by
        // the HR dashboard's "Document Reviews" widget. IsOverdue below distinguishes the two.
        var dueBy = today.AddDays(7);

        var documents = await db.SharedCompanyDocuments
            .AsNoTracking()
            .Where(d => d.CompanyId == request.CompanyId
                && d.Status != SharedCompanyDocumentStatus.Archived
                && d.ReviewDate != null
                && d.ReviewDate <= dueBy)
            .OrderBy(d => d.ReviewDate)
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
            .Select(d => new SharedCompanyDocumentDueForReviewItem(
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
                namesLookup.TryGetValue(d.UpdatedBy, out var updatedByName) ? updatedByName : "Unknown",
                d.ReviewDate < today))
            .ToList();

        return Result.Success(new ListSharedCompanyDocumentsDueForReviewResponse(items));
    }
}
