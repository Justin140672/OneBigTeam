using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

/// <summary>
/// Shared stage-change side effects used by every path that moves an Application to a new
/// RecruitmentStage (the generic MoveApplicationStage feature and the existing named-transition
/// handlers: RejectCandidate, WithdrawApplication, OfferCandidate, HireCandidate). Guarantees the
/// three effects required by tickets #65/#66/#67 happen exactly once per successful stage change:
///  1. A persisted ApplicationStageHistoryEntry (ticket #66) — added to the DbContext but not saved,
///     so callers can commit it in the same transaction as their own SaveChangesAsync.
///  2. An ApplicantStageChangedIntegrationEvent (ticket #65) — published only after the stage change
///     has actually been committed.
///  3. An ApplicationStageChangedAuditEvent (ticket #67) — published alongside the integration event.
/// Ticket #99: stages are now RecruitmentStage rows rather than ApplicationStatus enum values, so
/// this recorder resolves stage names for the human-readable integration/audit event payloads.
/// </summary>
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
            new ApplicantStageChangedIntegrationEvent(
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
