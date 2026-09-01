using System.Globalization;
using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Services;

/// <summary>
/// Story 2: contributes the Sickness module's principal data (sickness records, return-to-work
/// reviews, evidence-request metadata) to the organisation data export. Evidence file bytes are
/// never included — only metadata. company_id enforced on every query.
/// </summary>
internal sealed class SicknessDataExportSource(SicknessDbContext db) : ISicknessDataExportSource
{
    public async Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var records = await db.SicknessRecords.AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .Select(r => new { r.Id, r.EmployeeId, r.CategoryId, r.Status, r.StartDate, r.EndDate, r.ReturnToWorkDate, r.EvidenceStatus, r.TotalDays, r.CreatedAt })
            .ToListAsync(cancellationToken);

        var recordsTable = new DataExportTable(
            "sickness_records",
            ["Id", "EmployeeId", "CategoryId", "Status", "StartDate", "EndDate", "ReturnToWorkDate", "EvidenceStatus", "TotalDays", "CreatedAt"],
            records.Select(r => (IReadOnlyList<string?>)new string?[]
            {
                r.Id.ToString(), r.EmployeeId.ToString(), r.CategoryId.ToString(), r.Status.ToString(),
                D(r.StartDate), D(r.EndDate), D(r.ReturnToWorkDate), r.EvidenceStatus.ToString(),
                r.TotalDays?.ToString(CultureInfo.InvariantCulture), T(r.CreatedAt)
            }).ToList());

        var reviews = await db.ReturnToWorkReviews.AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .Select(r => new { r.Id, r.SicknessRecordId, r.EmployeeId, r.DueDate, r.ReviewedBy, r.Outcome, r.AdjustmentsRequired, r.Status, r.CompletedAt })
            .ToListAsync(cancellationToken);

        var reviewsTable = new DataExportTable(
            "return_to_work_reviews",
            ["Id", "SicknessRecordId", "EmployeeId", "DueDate", "ReviewedBy", "Outcome", "AdjustmentsRequired", "Status", "CompletedAt"],
            reviews.Select(r => (IReadOnlyList<string?>)new string?[]
            {
                r.Id.ToString(), r.SicknessRecordId.ToString(), r.EmployeeId.ToString(), D(r.DueDate),
                r.ReviewedBy?.ToString(), r.Outcome?.ToString(), r.AdjustmentsRequired ? "true" : "false",
                r.Status.ToString(), T(r.CompletedAt)
            }).ToList());

        var evidence = await db.SicknessEvidenceRequests.AsNoTracking()
            .Where(e => e.CompanyId == companyId)
            .Select(e => new { e.Id, e.SicknessRecordId, e.RequestedAt, e.DueDate, e.Status, e.FulfilledAt })
            .ToListAsync(cancellationToken);

        var evidenceTable = new DataExportTable(
            "sickness_evidence_requests",
            ["Id", "SicknessRecordId", "RequestedAt", "DueDate", "Status", "FulfilledAt"],
            evidence.Select(e => (IReadOnlyList<string?>)new string?[]
            {
                e.Id.ToString(), e.SicknessRecordId.ToString(), T(e.RequestedAt), D(e.DueDate), e.Status.ToString(), T(e.FulfilledAt)
            }).ToList());

        return [recordsTable, reviewsTable, evidenceTable];
    }

    private static string D(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string? D(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string T(DateTimeOffset value) => value.ToString("o", CultureInfo.InvariantCulture);
    private static string? T(DateTimeOffset? value) => value?.ToString("o", CultureInfo.InvariantCulture);
}
