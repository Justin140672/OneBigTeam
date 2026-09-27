using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

/// <summary>
/// Internal recruitment Ticket 7: the Recruitment half of an internal appointment, run once the
/// Employees module has recorded the employee change. Shared by AppointInternalCandidateHandler and
/// InternalAppointmentReconciliationJob so an interrupted appointment is completed identically however
/// it is recovered. Moves the application to the Hired stage (so it counts as a hire in reporting),
/// records stage history, then publishes <see cref="InternalCandidateAppointedIntegrationEvent"/> —
/// never <see cref="CandidateHiredIntegrationEvent"/>, whose consumers are new-hire side effects.
/// </summary>
internal sealed class InternalAppointmentCompleter(
    RecruitmentDbContext db,
    IClock clock,
    IIntegrationEventPublisher eventPublisher,
    IAuditEventPublisher auditPublisher,
    RecruitmentStageChangeRecorder recorder)
{
    public const string ConflictMessage = "This application was changed by someone else. Reload and try again.";

    public Task<Guid?> FindHiredStageIdAsync(Guid companyId, CancellationToken cancellationToken) =>
        db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.IsActive && s.TerminalOutcome == RecruitmentStageTerminalOutcome.Hired)
            .Select(s => (Guid?)s.Id)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Completes a Pending appointment on a tracked <paramref name="application"/>. Nothing is
    /// published unless the save commits; a concurrency conflict leaves the application Pending.
    /// </summary>
    public async Task<Result> CompleteAsync(
        Application application,
        Guid hiredStageId,
        InternalAppointmentResult appointment,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var previousStageId = application.CurrentStageId;
        var expectedVersion = application.Version;

        application.CompleteInternalAppointment(hiredStageId, appointment.PromotionId, appointment.EffectiveDate, now);
        recorder.AddHistoryEntry(application, previousStageId, performedBy, now, notes: "Internal appointment completed");

        var saveResult = await db.SaveChangesWithConcurrencyAsync(application, expectedVersion, ConflictMessage, cancellationToken);
        if (saveResult.IsFailure)
            return saveResult;

        await eventPublisher.PublishAsync(
            new InternalCandidateAppointedIntegrationEvent(
                application.CompanyId,
                application.Id,
                application.CandidateId,
                appointment.EmployeeId,
                application.VacancyId,
                appointment.PromotionId,
                appointment.EffectiveDate,
                appointment.IsApplied,
                now),
            cancellationToken);

        await recorder.PublishStageChangedEventsAsync(application, previousStageId, performedBy, now, cancellationToken);

        await auditPublisher.PublishAsync(
            new InternalCandidateAppointedAuditEvent(
                application.CompanyId,
                application.CandidateId,
                application.Id,
                application.VacancyId,
                appointment.EmployeeId,
                appointment.PromotionId,
                appointment.EffectiveDate,
                performedBy,
                now),
            cancellationToken);

        return Result.Success();
    }
}
