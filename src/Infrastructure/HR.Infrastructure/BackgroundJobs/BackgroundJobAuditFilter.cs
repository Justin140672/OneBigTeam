using Hangfire.Server;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.BackgroundJobs;

// IServerFilter is synchronous — SaveChanges() is used deliberately here.
internal sealed class BackgroundJobAuditFilter(
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundJobAuditFilter> logger) : IServerFilter
{
    private const string StartedAtKey = "hr_bg_started_at";

    public void OnPerforming(PerformingContext context)
    {
        context.Items[StartedAtKey] = DateTimeOffset.UtcNow;
    }

    public void OnPerformed(PerformedContext context)
    {
        if (context.Exception is null || context.ExceptionHandled)
            return;

        var occurredAt = context.Items.TryGetValue(StartedAtKey, out var val) && val is DateTimeOffset dt
            ? dt
            : DateTimeOffset.UtcNow;

        var auditEvent = new BackgroundJobFailedAuditEvent(
            jobId: context.BackgroundJob.Id,
            jobType: context.BackgroundJob.Job.Type.Name,
            methodName: context.BackgroundJob.Job.Method.Name,
            exception: context.Exception,
            occurredAt: occurredAt,
            companyId: ExtractCompanyId(context));

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            db.AuditEvents.Add(AuditEvent.From(auditEvent));
            db.SaveChanges();
        }
        catch (DbUpdateException ex) when (IsDuplicateEvent(ex))
        {
            // Deterministic EventId per Hangfire job id: a retry of the same failing job races to
            // insert the same row. One failure audit per job is intended — swallow the duplicate.
        }
        catch (Exception ex)
        {
            // OBT-REM-02: an audit persistence failure must never mask the original job failure —
            // Hangfire still sees context.Exception and applies its retry/error handling.
            logger.LogError(
                ex,
                "Failed to persist background-job failure audit for job {HangfireJobId}",
                context.BackgroundJob.Id);
        }
    }

    /// <summary>
    /// A tenant-aware job declares a <c>Guid companyId</c> parameter; pull the matching argument
    /// value so the audit row is scoped to that tenant. System-wide jobs have no such parameter.
    /// </summary>
    private static Guid? ExtractCompanyId(PerformedContext context)
    {
        var parameters = context.BackgroundJob.Job.Method.GetParameters();
        var args = context.BackgroundJob.Job.Args;

        for (var i = 0; i < parameters.Length && i < args.Count; i++)
        {
            if (parameters[i].ParameterType == typeof(Guid)
                && string.Equals(parameters[i].Name, "companyId", StringComparison.OrdinalIgnoreCase)
                && args[i] is Guid companyId
                && companyId != Guid.Empty)
            {
                return companyId;
            }
        }

        return null;
    }

    private static bool IsDuplicateEvent(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("ix_audit_events_event_id", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message.Contains("duplicate key value violates unique constraint", StringComparison.OrdinalIgnoreCase) == true;
}
