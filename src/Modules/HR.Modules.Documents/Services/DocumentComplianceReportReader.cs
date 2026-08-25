using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

internal sealed class DocumentComplianceReportReader(
    DocumentsDbContext dbContext,
    IEmployeeAudienceReader employeeAudienceReader,
    IPositionProfileDocumentsReader positionProfileDocumentsReader) : IDocumentComplianceReportReader
{
    public async Task<IReadOnlyList<DocumentComplianceReportItem>> GetDocumentComplianceReportAsync(
        Guid companyId,
        Guid? positionProfileId,
        CancellationToken cancellationToken)
    {
        var employeeIds = await employeeAudienceReader.GetEligibleEmployeeIdsAsync(
            companyId,
            departmentIds: [],
            locationIds: [],
            positionProfileIds: positionProfileId is null ? [] : [positionProfileId.Value],
            employeeIds: [],
            cancellationToken);

        if (employeeIds.Count == 0)
            return [];

        // Resolve each employee's own PositionProfileId — the report needs each employee's
        // required-document set, which depends on their assigned position profile. Employees with
        // no position profile at all have no required-document set and are omitted from the
        // result entirely (simplest way to keep the comparison well-defined).
        // Bulk call (OBT-720 perf pass) — replaces a former per-employee loop over
        // IEmployeeAudienceReader.GetEmployeeAudienceAsync(companyId, employeeId, ...), which issued
        // one query per employee in this report.
        var audienceProfiles = await employeeAudienceReader.GetEmployeeAudienceProfilesAsync(
            companyId, employeeIds, cancellationToken);

        var employeePositionProfiles = new Dictionary<Guid, Guid>();
        foreach (var employeeId in employeeIds)
        {
            if (audienceProfiles.TryGetValue(employeeId, out var profile) && profile.PositionProfileId is { } ppId)
                employeePositionProfiles[employeeId] = ppId;
        }

        if (employeePositionProfiles.Count == 0)
            return [];

        // Cache/dedupe required-document lookups within one report run — many employees will
        // share a position profile.
        var requiredDocsByProfile = new Dictionary<Guid, IReadOnlyList<PositionProfileRequiredDocumentItem>>();
        foreach (var ppId in employeePositionProfiles.Values.Distinct())
        {
            requiredDocsByProfile[ppId] =
                await positionProfileDocumentsReader.GetActiveDocumentsAsync(companyId, ppId, cancellationToken);
        }

        var documentTypeIds = requiredDocsByProfile.Values
            .SelectMany(list => list.Select(d => d.DocumentTypeId))
            .Distinct()
            .ToList();

        var documentTypeNames = await dbContext.DocumentTypes
            .AsNoTracking()
            .Where(dt => dt.CompanyId == companyId && documentTypeIds.Contains(dt.Id))
            .Select(dt => new { dt.Id, dt.Name })
            .ToDictionaryAsync(dt => dt.Id, dt => dt.Name, cancellationToken);

        var relevantEmployeeIds = employeePositionProfiles.Keys.ToList();

        var uploaded = await (
            from ed in dbContext.EmployeeDocuments.AsNoTracking()
            join d in dbContext.Documents.AsNoTracking() on ed.DocumentId equals d.Id
            where ed.CompanyId == companyId && relevantEmployeeIds.Contains(ed.EmployeeId)
            select new { ed.EmployeeId, d.DocumentTypeId, ed.ExpiryDate })
            .ToListAsync(cancellationToken);

        var uploadedByEmployee = uploaded.ToLookup(u => u.EmployeeId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var results = new List<DocumentComplianceReportItem>();

        foreach (var (employeeId, positionProfileIdValue) in employeePositionProfiles)
        {
            var required = requiredDocsByProfile[positionProfileIdValue];
            var employeeUploads = uploadedByEmployee[employeeId].ToList();

            var uploadedCount = 0;
            var expiringSoonCount = 0;
            var expiredCount = 0;
            var missingTypeNames = new List<string>();

            foreach (var requiredDoc in required)
            {
                var match = employeeUploads.FirstOrDefault(u => u.DocumentTypeId == requiredDoc.DocumentTypeId);
                if (match is null)
                {
                    missingTypeNames.Add(documentTypeNames.TryGetValue(requiredDoc.DocumentTypeId, out var name)
                        ? name
                        : requiredDoc.DocumentTypeId.ToString());
                    continue;
                }

                uploadedCount++;

                var status = match.ExpiryDate switch
                {
                    null => DocumentExpiryStatus.Valid,
                    var expiry when expiry < today => DocumentExpiryStatus.Expired,
                    var expiry when expiry <= today.AddDays(30) => DocumentExpiryStatus.ExpiringSoon,
                    _ => DocumentExpiryStatus.Valid,
                };

                if (status == DocumentExpiryStatus.Expired)
                    expiredCount++;
                else if (status == DocumentExpiryStatus.ExpiringSoon)
                    expiringSoonCount++;
            }

            results.Add(new DocumentComplianceReportItem(
                employeeId,
                positionProfileIdValue,
                required.Count,
                uploadedCount,
                missingTypeNames.Count,
                expiringSoonCount,
                expiredCount,
                missingTypeNames));
        }

        // Deterministic ordering with an explicit tiebreaker (REP-05) — a Dictionary's enumeration
        // order is an implementation detail, not a guarantee.
        return results
            .OrderBy(r => r.EmployeeId)
            .ToList();
    }
}
