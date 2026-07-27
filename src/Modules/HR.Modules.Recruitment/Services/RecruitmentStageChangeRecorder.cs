using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;

namespace HR.Modules.Recruitment.Services;

/// <summary>
/// Shared stage-change side effects used by every path that moves an Application to a new
/// ApplicationStatus (the generic MoveApplicationStage feature and the existing named-transition
/// handlers: RejectCandidate, WithdrawApplication, OfferCandidate, HireCandidate, ScheduleInterview,
/// RecordInterviewOutcome). Guarantees the three effects required by tickets #65/#66/#67 happen
/// exactly once per successful stage change:
///  1. A persisted ApplicationStageHistoryEntry (ticket #66) — added to the DbContext but not saved,
///     so callers can commit it in the same transaction as their own SaveChangesAsync.
///  2. An ApplicantStageChangedIntegrationEvent (ticket #65) — published only after the stage change
///     has actually been committed.
///  3. An ApplicationStageChangedAuditEvent (ticket #67) — published alongside the integration event.
/// </summary>
internal sealed class RecruitmentStageChangeRecorder(
    RecruitmentDbContext db,
    IIntegrationEventPublisher eventPublisher,
    IAuditEventPublisher auditPublisher)
{
    public void AddHistoryEntry(
        Application application,
        ApplicationStatus previousStage,
        Guid? changedByUserId,
        DateTimeOffset now,
        string? notes = null)
    {
        db.ApplicationStageHistoryEntries.Add(ApplicationStageHistoryEntry.Create(
            Guid.NewGuid(),
            application.CompanyId,
            application.Id,
            previousStage,
            application.Status,
            changedByUserId,
            notes,
            now));
    }

    public async Task PublishStageChangedEventsAsync(
        Application application,
        ApplicationStatus previousStage,
        Guid changedBy,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await eventPublisher.PublishAsync(
            new ApplicantStageChangedIntegrationEvent(
                application.CompanyId,
                application.Id,
                application.VacancyId,
                previousStage.ToString(),
                application.Status.ToString(),
                changedBy,
                now),
            cancellationToken);

        await auditPublisher.PublishAsync(
            new ApplicationStageChangedAuditEvent(
                application.CompanyId,
                application.Id,
                application.VacancyId,
                application.CandidateId,
                previousStage,
                application.Status,
                changedBy,
                now),
            cancellationToken);
    }
}
