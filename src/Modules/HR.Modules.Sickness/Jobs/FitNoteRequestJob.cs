using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Jobs;

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
