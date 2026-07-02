using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Jobs;

internal sealed class FitNoteRequestJob(
    SicknessDbContext db,
    ICompanySicknessSettingsReader sicknessSettingsReader,
    IClock clock)
{
    private static readonly Guid SystemUserId = Guid.Empty;
    private const int DueDateDaysFromNow = 7;

    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var companyIds = await db.SicknessRecords
            .AsNoTracking()
            .Where(r => r.EndDate == null && r.Status == SicknessStatus.Active)
            .Select(r => r.CompanyId)
            .Distinct()
            .ToListAsync();

        foreach (var companyId in companyIds)
        {
            var settings = await sicknessSettingsReader.GetSicknessSettingsAsync(companyId, CancellationToken.None);

            if (settings.FitNoteRequiredAfterDays is null)
                continue;

            var threshold = settings.FitNoteRequiredAfterDays.Value;

            var eligibleRecords = await db.SicknessRecords
                .Where(r =>
                    r.CompanyId == companyId &&
                    r.EndDate == null &&
                    r.Status == SicknessStatus.Active &&
                    r.EvidenceStatus == SicknessEvidenceStatus.Pending &&
                    r.TotalDays >= threshold)
                .ToListAsync();

            if (eligibleRecords.Count == 0)
                continue;

            var recordIds = eligibleRecords.Select(r => r.Id).ToList();

            var existingRequestRecordIds = await db.SicknessEvidenceRequests
                .AsNoTracking()
                .Where(e =>
                    recordIds.Contains(e.SicknessRecordId) &&
                    e.Status != SicknessEvidenceRequestStatus.Cancelled)
                .Select(e => e.SicknessRecordId)
                .ToHashSetAsync();

            var dueDate = today.AddDays(DueDateDaysFromNow);

            foreach (var record in eligibleRecords)
            {
                if (existingRequestRecordIds.Contains(record.Id))
                    continue;

                var evidenceRequest = SicknessEvidenceRequest.Create(
                    Guid.NewGuid(),
                    companyId,
                    record.Id,
                    SystemUserId,
                    dueDate,
                    null,
                    now);

                db.SicknessEvidenceRequests.Add(evidenceRequest);
            }

            await db.SaveChangesAsync();
        }
    }
}
