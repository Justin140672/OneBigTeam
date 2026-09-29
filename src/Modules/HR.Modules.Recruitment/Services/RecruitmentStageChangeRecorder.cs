using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

internal sealed class RecruitmentStageChangeRecorder(
    RecruitmentDbContext db,
    IIntegrationEventPublisher eventPublisher,
    IAuditEventPublisher auditPublisher)
{
    public void AddHistoryEntry(
        Application application,
        Guid previousStageId,
        Guid? changedByUserId,
        DateTimeOffset now,
        string? notes = null)
    {
        db.ApplicationStageHistoryEntries.Add(ApplicationStageHistoryEntry.Create(
            Guid.NewGuid(),
            application.CompanyId,
            application.Id,
            previousStageId,
            application.CurrentStageId,
            changedByUserId,
            notes,
            now));
    }

    public async Task PublishStageChangedEventsAsync(
        Application application,
        Guid previousStageId,
        Guid changedBy,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var stageNames = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.Id == previousStageId || s.Id == application.CurrentStageId)
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        var previousStageName = stageNames.GetValueOrDefault(previousStageId, previousStageId.ToString());
        var newStageName      = stageNames.GetValueOrDefault(application.CurrentStageId, application.CurrentStageId.ToString());

        await eventPublisher.PublishAsync(
            new ApplicationStageChangedIntegrationEvent(
                application.CompanyId,
                application.Id,
                application.VacancyId,
                previousStageName,
                newStageName,
                changedBy,
                now),
            cancellationToken);

        await auditPublisher.PublishAsync(
            new ApplicationStageChangedAuditEvent(
                application.CompanyId,
                application.Id,
                application.VacancyId,
                application.CandidateId,
                previousStageId,
                previousStageName,
                application.CurrentStageId,
                newStageName,
                changedBy,
                now),
            cancellationToken);
    }
}
