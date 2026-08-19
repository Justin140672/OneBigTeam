using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Jobs;

internal sealed class FitNoteRequestJob(
    SicknessDbContext db,
    ICompanySicknessSettingsReader sicknessSettingsReader,
    IIntegrationEventPublisher eventPublisher,
    IAuditEventPublisher auditPublisher,
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

            // Mandatory, always set (no opt-out) — see CompanySettings.FitNoteRequiredAfterDays.
            var threshold = settings.FitNoteRequiredAfterDays;

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

            var newRequests = new List<(SicknessEvidenceRequest Request, Guid EmployeeId)>();

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
                newRequests.Add((evidenceRequest, record.EmployeeId));
            }

            await db.SaveChangesAsync();

            foreach (var (request, employeeId) in newRequests)
            {
                await eventPublisher.PublishAsync(
                    new SicknessEvidenceRequestedIntegrationEvent(
                        CompanyId:        companyId,
                        EmployeeId:       employeeId,
                        SicknessRecordId: request.SicknessRecordId,
                        EvidenceRequestId: request.Id,
                        DueDate:          dueDate,
                        OccurredAt:       now),
                    CancellationToken.None);

                await auditPublisher.PublishAsync(
                    new SicknessEvidenceRequestedAuditEvent(
                        EvidenceRequestId: request.Id,
                        SicknessRecordId:  request.SicknessRecordId,
                        CompanyId:         companyId,
                        EmployeeId:        employeeId,
                        DueDate:           dueDate,
                        OccurredAt:        now),
                    CancellationToken.None);
            }
        }
    }
}
