using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetSharedCompanyDocument;

internal sealed class GetSharedCompanyDocumentHandler(
    DocumentsDbContext db,
    IEmployeeNameReader employeeNameReader,
    SharedCompanyDocumentAudienceMatcher audienceMatcher,
    SharedCompanyDocumentAudienceDescriber audienceDescriber)
{
    public async Task<Result<GetSharedCompanyDocumentResponse>> HandleAsync(
        GetSharedCompanyDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var document = await db.SharedCompanyDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<GetSharedCompanyDocumentResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        var category = await db.CompanyDocumentCategories
            .AsNoTracking()
            .Where(c => c.Id == document.CategoryId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var versions = await db.SharedCompanyDocumentVersions
            .AsNoTracking()
            .Where(v => v.SharedCompanyDocumentId == document.Id)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);

        // ThenByDescending(CreatedAt) breaks ties deterministically when two reviews are completed
        // on the same calendar day (ReviewDate has only day granularity) — without it, "newest
        // first" would be unspecified/non-deterministic for same-day reviews.
        var reviewHistoryRows = await db.SharedCompanyDocumentReviewHistories
            .AsNoTracking()
            .Where(h => h.SharedCompanyDocumentId == document.Id)
            .OrderByDescending(h => h.ReviewDate)
            .ThenByDescending(h => h.CreatedAt)
            .ToListAsync(cancellationToken);

        var uploaderIds = versions.Select(v => v.CreatedBy).Distinct().ToList();
        var reviewerIds = reviewHistoryRows.Select(h => h.ReviewedByEmployeeId).Distinct().ToList();
        var idsToResolve = new List<Guid> { document.CreatedBy, document.UpdatedBy };
        if (document.PublishedBy is { } publishedBy)
            idsToResolve.Add(publishedBy);
        if (document.ArchivedBy is { } archivedBy)
            idsToResolve.Add(archivedBy);
        if (document.ReviewOwnerEmployeeId is { } reviewOwnerEmployeeId)
            idsToResolve.Add(reviewOwnerEmployeeId);
        if (document.LastReviewedByEmployeeId is { } lastReviewedByEmployeeId)
            idsToResolve.Add(lastReviewedByEmployeeId);
        idsToResolve.AddRange(uploaderIds);
        idsToResolve.AddRange(reviewerIds);

        var namesLookup = await employeeNameReader.GetNamesAsync(
            request.CompanyId,
            idsToResolve,
            cancellationToken);

        var versionHistory = versions
            .Select(v => new SharedCompanyDocumentVersionItem(
                v.VersionNumber,
                v.FileName,
                v.FileSize,
                namesLookup.TryGetValue(v.CreatedBy, out var uploaderName) ? uploaderName : "Unknown",
                v.CreatedAt,
                v.VersionNote,
                v.RequiresAcknowledgement,
                v.EffectiveDate,
                v.VersionNumber == document.VersionNumber ? document.Status.ToString() : "Superseded"))
            .ToList();

        var reviewHistory = reviewHistoryRows
            .Select(h => new SharedCompanyDocumentReviewHistoryItem(
                h.ReviewDate,
                h.ReviewedByEmployeeId,
                namesLookup.TryGetValue(h.ReviewedByEmployeeId, out var reviewerName) ? reviewerName : "Unknown",
                h.ReviewNotes,
                h.PreviousReviewDate))
            .ToList();

        var (audienceDepartmentIds, audienceLocationIds, audiencePositionProfileIds, audienceEmployeeIds) =
            await audienceMatcher.GetRuleTargetsByTypeAsync(document.Id, cancellationToken);

        var audienceDescription = await audienceDescriber.DescribeAsync(
            request.CompanyId, audienceDepartmentIds, audienceLocationIds, audiencePositionProfileIds, audienceEmployeeIds, cancellationToken);

        AcknowledgementProgressInfo? acknowledgementProgress = null;
        if (document.RequiresAcknowledgement)
        {
            var eligibleIds = await audienceMatcher.GetEligibleEmployeeIdsAsync(request.CompanyId, document.Id, cancellationToken);

            var acknowledgedEmployeeIds = await db.SharedCompanyDocumentAcknowledgements
                .AsNoTracking()
                .Where(a => a.SharedCompanyDocumentId == document.Id && a.VersionNumber == document.VersionNumber)
                .Select(a => a.EmployeeId)
                .ToListAsync(cancellationToken);

            // Only count acknowledgements from employees who are still within the current
            // audience — someone who acknowledged before being moved out of scope shouldn't
            // inflate the progress count.
            var relevantAcknowledgedIds = acknowledgedEmployeeIds.Intersect(eligibleIds).ToList();
            var acknowledgedNames = await employeeNameReader.GetNamesAsync(request.CompanyId, relevantAcknowledgedIds, cancellationToken);

            acknowledgementProgress = new AcknowledgementProgressInfo(
                relevantAcknowledgedIds.Count,
                eligibleIds.Count,
                acknowledgedNames.Values.OrderBy(n => n).ToList());
        }

        return Result.Success(new GetSharedCompanyDocumentResponse(
            document.Id,
            document.CompanyId,
            document.Title,
            document.Description,
            document.CategoryId,
            category ?? "Unknown",
            document.FileName,
            document.FileSize,
            document.ContentType,
            document.VersionNumber,
            document.Status.ToString(),
            document.EffectiveDate,
            document.ReviewDate,
            document.ReviewFrequency.ToString(),
            document.CustomReviewFrequencyMonths,
            document.ReviewOwnerEmployeeId,
            document.ReviewOwnerEmployeeId is { } ownerEmployeeId
                ? (namesLookup.TryGetValue(ownerEmployeeId, out var reviewOwnerName) ? reviewOwnerName : "Unknown")
                : null,
            audienceDescription,
            audienceDepartmentIds,
            audienceLocationIds,
            audiencePositionProfileIds,
            audienceEmployeeIds,
            document.RequiresAcknowledgement,
            document.AcknowledgementDueDate,
            document.AcknowledgementStatement,
            acknowledgementProgress,
            versionHistory,
            namesLookup.TryGetValue(document.CreatedBy, out var createdByName) ? createdByName : "Unknown",
            document.CreatedAt,
            namesLookup.TryGetValue(document.UpdatedBy, out var updatedByName) ? updatedByName : "Unknown",
            document.UpdatedAt,
            document.PublishedBy is { } pubBy ? (namesLookup.TryGetValue(pubBy, out var publishedByName) ? publishedByName : "Unknown") : null,
            document.PublishedAt,
            document.ArchivedBy is { } archBy ? (namesLookup.TryGetValue(archBy, out var archivedByName) ? archivedByName : "Unknown") : null,
            document.ArchivedAt,
            document.ArchiveReason,
            document.LastReviewedAt,
            document.LastReviewedByEmployeeId,
            document.LastReviewedByEmployeeId is { } lastReviewerId
                ? (namesLookup.TryGetValue(lastReviewerId, out var lastReviewedByName) ? lastReviewedByName : "Unknown")
                : null,
            document.LastReviewNotes,
            reviewHistory));
    }
}
