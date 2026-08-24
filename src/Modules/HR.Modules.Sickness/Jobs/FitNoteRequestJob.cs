using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Jobs;

/// <summary>
/// Daily job (SICK-01) that re-evaluates sickness records' calendar-day duration against the
/// company's configured FitNoteRequiredAfterDays threshold and creates a fit-note evidence request
/// (see FitNoteEvidenceRequestService) the first time a record reaches it.
///
/// Two passes per company:
///   1. Open (Active, EndDate == null) records are evaluated against "today". This is what actually
///      detects an ongoing absence crossing the threshold — duration grows day over day, so a
///      record that wasn't eligible yesterday may be eligible today.
///   2. Closed records that don't yet have a live evidence request are evaluated against their own
///      EndDate — a defence-in-depth catch-all for a record closed before this job last ran. The
///      RecordSickness/RecordMySickness and CloseSicknessRecord handlers already perform this same
///      evaluation immediately (via FitNoteEvidenceRequestService) at creation/close time, so this
///      pass is normally a no-op, but it still covers imported/backdated data and any write path
///      that bypasses those handlers.
///
/// Entirely idempotent: FitNoteEvidenceRequestService.RequestIfEligibleAsync checks for an existing
/// live request before creating one, so re-running this job — including a Hangfire retry after a
/// partial failure — never creates duplicate requests, tasks, notifications or audit events.
/// Received/Waived records are always skipped and never re-requested.
/// </summary>
internal sealed class FitNoteRequestJob(
    SicknessDbContext db,
    ICompanySicknessSettingsReader sicknessSettingsReader,
    FitNoteEvidenceRequestService evidenceRequestService,
    IClock clock)
{
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var companyIds = await db.SicknessRecords
            .AsNoTracking()
            .Where(r =>
                r.EvidenceStatus != SicknessEvidenceStatus.Received &&
                r.EvidenceStatus != SicknessEvidenceStatus.Waived)
            .Select(r => r.CompanyId)
            .Distinct()
            .ToListAsync();

        foreach (var companyId in companyIds)
        {
            var settings = await sicknessSettingsReader.GetSicknessSettingsAsync(companyId, CancellationToken.None);

            // Mandatory, always set (no opt-out) — see CompanySettings.FitNoteRequiredAfterDays.
            var threshold = settings.FitNoteRequiredAfterDays;

            await EvaluateOpenRecordsAsync(companyId, threshold, today, now);
            await EvaluateUnrequestedClosedRecordsAsync(companyId, threshold, now);
        }
    }

    private async Task EvaluateOpenRecordsAsync(Guid companyId, int threshold, DateOnly today, DateTimeOffset now)
    {
        var openRecords = await db.SicknessRecords
            .Where(r =>
                r.CompanyId == companyId &&
                r.EndDate == null &&
                r.Status == SicknessStatus.Active &&
                r.EvidenceStatus != SicknessEvidenceStatus.Received &&
                r.EvidenceStatus != SicknessEvidenceStatus.Waived)
            .ToListAsync();

        foreach (var record in openRecords)
        {
            await evidenceRequestService.RequestIfEligibleAsync(
                record, threshold, today, now, CancellationToken.None);
        }
    }

    private async Task EvaluateUnrequestedClosedRecordsAsync(Guid companyId, int threshold, DateTimeOffset now)
    {
        var liveRequestRecordIds = await db.SicknessEvidenceRequests
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Status != SicknessEvidenceRequestStatus.Cancelled)
            .Select(e => e.SicknessRecordId)
            .ToListAsync();

        var closedRecordsWithoutRequest = await db.SicknessRecords
            .Where(r =>
                r.CompanyId == companyId &&
                r.EndDate != null &&
                r.Status == SicknessStatus.Closed &&
                r.EvidenceStatus != SicknessEvidenceStatus.Received &&
                r.EvidenceStatus != SicknessEvidenceStatus.Waived &&
                !liveRequestRecordIds.Contains(r.Id))
            .ToListAsync();

        foreach (var record in closedRecordsWithoutRequest)
        {
            await evidenceRequestService.RequestIfEligibleAsync(
                record, threshold, record.EndDate!.Value, now, CancellationToken.None);
        }
    }
}
