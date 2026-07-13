using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetSharedCompanyDocument;

internal sealed class GetSharedCompanyDocumentHandler(
    DocumentsDbContext db,
    IEmployeeNameReader employeeNameReader,
    IEmployeeAudienceReader audienceReader)
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

        var uploaderIds = versions.Select(v => v.CreatedBy).Distinct().ToList();
        var namesLookup = await employeeNameReader.GetNamesAsync(request.CompanyId, [document.CreatedBy, document.UpdatedBy, .. uploaderIds], cancellationToken);

        var versionHistory = versions
            .Select(v => new SharedCompanyDocumentVersionItem(
                v.VersionNumber,
                v.FileName,
                v.FileSize,
                namesLookup.TryGetValue(v.CreatedBy, out var uploaderName) ? uploaderName : "Unknown",
                v.CreatedAt))
            .ToList();

        var audienceDescription = await DescribeAudienceAsync(request.CompanyId, document.AudienceDepartmentId, document.AudienceLocationId, cancellationToken);

        AcknowledgementProgressInfo? acknowledgementProgress = null;
        if (document.RequiresAcknowledgement)
        {
            var eligibleIds = await audienceReader.GetEligibleEmployeeIdsAsync(
                request.CompanyId, document.AudienceDepartmentId, document.AudienceLocationId, cancellationToken);

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
            audienceDescription,
            document.RequiresAcknowledgement,
            acknowledgementProgress,
            versionHistory,
            namesLookup.TryGetValue(document.CreatedBy, out var createdByName) ? createdByName : "Unknown",
            document.CreatedAt,
            namesLookup.TryGetValue(document.UpdatedBy, out var updatedByName) ? updatedByName : "Unknown",
            document.UpdatedAt));
    }

    private async Task<string> DescribeAudienceAsync(
        Guid companyId, Guid? departmentId, Guid? locationId, CancellationToken cancellationToken)
    {
        if (departmentId is not null)
        {
            var name = await audienceReader.GetDepartmentNameAsync(companyId, departmentId.Value, cancellationToken);
            return $"Department: {name ?? "Unknown"}";
        }

        if (locationId is not null)
        {
            var name = await audienceReader.GetLocationNameAsync(companyId, locationId.Value, cancellationToken);
            return $"Location: {name ?? "Unknown"}";
        }

        return "All Employees";
    }
}
