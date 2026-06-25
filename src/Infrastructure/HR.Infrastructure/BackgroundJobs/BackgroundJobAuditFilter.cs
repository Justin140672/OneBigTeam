using Hangfire.Server;
using HR.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Infrastructure.BackgroundJobs;

// IServerFilter is synchronous — SaveChanges() is used deliberately here.
internal sealed class BackgroundJobAuditFilter(IServiceScopeFactory scopeFactory) : IServerFilter
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
            occurredAt: occurredAt);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        db.AuditEvents.Add(AuditEvent.From(auditEvent));
        db.SaveChanges();
    }
}
