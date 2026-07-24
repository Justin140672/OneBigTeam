using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetSharedCompanyDocumentAcknowledgementProgress;

internal sealed class GetSharedCompanyDocumentAcknowledgementProgressHandler(
    DocumentsDbContext db,
    SharedCompanyDocumentAudienceMatcher audienceMatcher,
    IEmployeeAudienceReader audienceReader,
    IEmployeeNameReader employeeNameReader,
    IClock clock)
{
    private const string StatusAcknowledged = "Acknowledged";
    private const string StatusOutstanding = "Outstanding";
    private const string StatusOverdue = "Overdue";

    public async Task<Result<GetSharedCompanyDocumentAcknowledgementProgressResponse>> HandleAsync(
        GetSharedCompanyDocumentAcknowledgementProgressRequest request,
        CancellationToken cancellationToken)
    {
        var document = await db.SharedCompanyDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<GetSharedCompanyDocumentAcknowledgementProgressResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        if (!document.RequiresAcknowledgement)
            return Result.Failure<GetSharedCompanyDocumentAcknowledgementProgressResponse>(
                Error.Validation("This document does not require acknowledgement."));

        var eligibleIds = await audienceMatcher.GetEligibleEmployeeIdsAsync(request.CompanyId, document.Id, cancellationToken);

        var acknowledgementsByEmployeeId = await db.SharedCompanyDocumentAcknowledgements
            .AsNoTracking()
            .Where(a => a.SharedCompanyDocumentId == document.Id && a.VersionNumber == document.VersionNumber)
            .ToDictionaryAsync(a => a.EmployeeId, a => a, cancellationToken);

        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, eligibleIds, cancellationToken);
        var details = await audienceReader.GetEmployeeAudienceDetailsAsync(request.CompanyId, eligibleIds, cancellationToken);
        var detailsByEmployeeId = details.ToDictionary(d => d.EmployeeId);

        var today = DateOnly.FromDateTime(clock.UtcNowOffset().UtcDateTime);

        var allItems = eligibleIds.Select(employeeId =>
        {
            acknowledgementsByEmployeeId.TryGetValue(employeeId, out var acknowledgement);
            var acknowledgedAt = acknowledgement?.AcknowledgedAt;
            var isOverdue = acknowledgedAt is null && document.AcknowledgementDueDate is not null && document.AcknowledgementDueDate < today;
            var status = acknowledgedAt is not null ? StatusAcknowledged : isOverdue ? StatusOverdue : StatusOutstanding;

            detailsByEmployeeId.TryGetValue(employeeId, out var detail);

            return new SharedCompanyDocumentAcknowledgementProgressItem(
                employeeId,
                names.TryGetValue(employeeId, out var name) ? name : "Unknown",
                detail?.DepartmentId,
                detail?.DepartmentName,
                detail?.LocationId,
                detail?.LocationName,
                detail?.ManagerId,
                detail?.ManagerName,
                status,
                document.AcknowledgementDueDate,
                acknowledgedAt,
                acknowledgement is not null ? acknowledgement.VersionNumber : null,
                acknowledgement?.AcknowledgementStatement);
        }).ToList();

        var totalAssigned = allItems.Count;
        var acknowledgedCount = allItems.Count(i => i.Status == StatusAcknowledged);
        var overdueCount = allItems.Count(i => i.Status == StatusOverdue);
        var outstandingCount = allItems.Count(i => i.Status == StatusOutstanding);
        var percentage = totalAssigned == 0 ? 0m : Math.Round(acknowledgedCount * 100m / totalAssigned, 1);

        var filteredItems = allItems.Where(i =>
                (request.DepartmentId is null || i.DepartmentId == request.DepartmentId) &&
                (request.LocationId is null || i.LocationId == request.LocationId) &&
                (request.IsAcknowledged is null || (i.Status == StatusAcknowledged) == request.IsAcknowledged) &&
                (request.IsOverdue is null || (i.Status == StatusOverdue) == request.IsOverdue))
            .OrderBy(i => i.EmployeeName)
            .ToList();

        return Result.Success(new GetSharedCompanyDocumentAcknowledgementProgressResponse(
            document.Id,
            document.Title,
            totalAssigned,
            acknowledgedCount,
            outstandingCount,
            overdueCount,
            percentage,
            filteredItems));
    }
}
