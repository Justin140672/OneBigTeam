using Hangfire;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Jobs;

/// <summary>
/// Internal recruitment Ticket 7: recovers internal appointments left Pending by an interrupted
/// request (crash, timeout, lost connection) — the "committed in one module, missing in the other"
/// case. For each application Pending for longer than <see cref="StaleAfter"/> (so an in-flight
/// request is never raced):
///  - if the Employees module recorded the change (looked up by the application's stable source
///    reference), the application is completed exactly as the request would have completed it;
///  - otherwise the Employees side never committed, so the Pending marker is released and HR can
///    retry the appointment.
/// Idempotent and safe to run repeatedly or concurrently: completion and release are both guarded by
/// the application's optimistic-concurrency version, and a completed application is never Pending.
/// </summary>
internal sealed class InternalAppointmentReconciliationJob(
    RecruitmentDbContext db,
    IEmployeeInternalAppointmentService appointmentService,
    InternalAppointmentCompleter completer,
    IClock clock,
    ILogger<InternalAppointmentReconciliationJob> logger)
{
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
    private const int BatchSize = 100;

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var cutoff = clock.UtcNowOffset() - StaleAfter;

        var pendingIds = await db.Applications
            .AsNoTracking()
            .Where(a => a.AppointmentStatus == InternalAppointmentStatus.Pending && a.AppointmentRequestedAt < cutoff)
            .OrderBy(a => a.AppointmentRequestedAt)
            .Select(a => a.Id)
            .Take(BatchSize)
            .ToListAsync();

        foreach (var applicationId in pendingIds)
        {
            try
            {
                await ReconcileAsync(applicationId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Internal appointment reconciliation failed for application {ApplicationId}; it will be retried on the next run.",
                    applicationId);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    internal async Task ReconcileAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        if (application is null || application.AppointmentStatus != InternalAppointmentStatus.Pending)
            return;

        var performedBy = application.AppointmentRequestedByUserId ?? Guid.Empty;

        var recorded = await appointmentService.ResumeBySourceReferenceAsync(
            application.CompanyId, application.InternalAppointmentSourceReference, performedBy, cancellationToken);

        if (recorded is null)
        {
            application.AbandonInternalAppointment(clock.UtcNowOffset());
            await db.SaveChangesWithConcurrencyAsync(
                application, application.Version, InternalAppointmentCompleter.ConflictMessage, cancellationToken);

            logger.LogWarning(
                "Released stale pending internal appointment on application {ApplicationId} in company {CompanyId}: no employee change was recorded.",
                application.Id, application.CompanyId);
            return;
        }

        var hiredStageId = await completer.FindHiredStageIdAsync(application.CompanyId, cancellationToken);
        if (hiredStageId is null)
        {
            logger.LogWarning(
                "Cannot complete internal appointment on application {ApplicationId} in company {CompanyId}: no active Hired stage is configured.",
                application.Id, application.CompanyId);
            return;
        }

        var completion = await completer.CompleteAsync(application, hiredStageId.Value, recorded, performedBy, cancellationToken);

        if (completion.IsSuccess)
            logger.LogInformation(
                "Completed interrupted internal appointment on application {ApplicationId} in company {CompanyId} (promotion {PromotionId}).",
                application.Id, application.CompanyId, recorded.PromotionId);
    }
}
