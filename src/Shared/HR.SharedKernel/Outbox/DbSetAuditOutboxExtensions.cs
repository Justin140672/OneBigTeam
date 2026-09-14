using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.SharedKernel.Outbox;

/// <summary>
/// Ticket 3 (P1) follow-up item 5: stage-then-dispatch helpers for a module's own
/// <see cref="IAuditOutboxEntry"/> entity. See that interface for the atomicity rationale.
/// </summary>
public static class DbSetAuditOutboxExtensions
{
    private const int MaxAttemptsBeforeTerminal = 10;

    /// <summary>
    /// Stages an outbox entry for <paramref name="auditEvent"/> on <paramref name="outbox"/> - call
    /// this instead of <c>IAuditEventPublisher.PublishAsync</c> directly, then let the caller's own
    /// SaveChangesAsync (or <c>SaveIdempotentAsync</c>) commit it together with the business write.
    /// </summary>
    public static void EnqueueAuditOutbox<TEntry, TAuditEvent>(
        this DbSet<TEntry> outbox,
        TAuditEvent auditEvent,
        Guid companyId,
        DateTimeOffset now)
        where TEntry : class, IAuditOutboxEntry, new() =>
        Enqueue(outbox, OutboxChannel.Audit, auditEvent, companyId, now);

    /// <summary>
    /// Stages an outbox entry for <paramref name="integrationEvent"/> - call this instead of
    /// <c>IIntegrationEventPublisher.PublishAsync</c> directly. Use for cross-module events (e.g.
    /// employee creation) whose downstream consumers (onboarding, probation, leave, tasks,
    /// notifications, ...) must not be silently skipped by a crash between commit and delivery.
    /// </summary>
    public static void EnqueueIntegrationOutbox<TEntry, TIntegrationEvent>(
        this DbSet<TEntry> outbox,
        TIntegrationEvent integrationEvent,
        Guid companyId,
        DateTimeOffset now)
        where TEntry : class, IAuditOutboxEntry, new() =>
        Enqueue(outbox, OutboxChannel.Integration, integrationEvent, companyId, now);

    private static void Enqueue<TEntry, TEvent>(
        DbSet<TEntry> outbox, string channel, TEvent @event, Guid companyId, DateTimeOffset now)
        where TEntry : class, IAuditOutboxEntry, new()
    {
        outbox.Add(new TEntry
        {
            Id = Guid.NewGuid(),
            Channel = channel,
            EventTypeName = typeof(TEvent).AssemblyQualifiedName!,
            PayloadJson = JsonSerializer.Serialize(@event),
            CompanyId = companyId,
            CreatedAt = now,
        });
    }

    /// <summary>
    /// Delivers one bounded batch of due, undelivered entries - audit-channel entries via
    /// <paramref name="auditPublisher"/>, integration-channel entries via
    /// <paramref name="integrationPublisher"/> (omit if this module never enqueues integration
    /// events) - durably tracking success, attempt count, next-attempt backoff, and terminal
    /// failure. Safe to call repeatedly (e.g. from a recurring background job) - a delivery that
    /// already succeeded is never re-sent, and one that keeps failing is retried with exponential
    /// backoff up to <see cref="MaxAttemptsBeforeTerminal"/> attempts before being marked terminally
    /// failed for an operator to investigate (its row is kept, not deleted, so nothing is silently
    /// lost).
    /// </summary>
    public static async Task<int> DispatchPendingAsync<TEntry>(
        this DbContext dbContext,
        DbSet<TEntry> outbox,
        IAuditEventPublisher auditPublisher,
        DateTimeOffset now,
        int batchSize,
        ILogger logger,
        CancellationToken cancellationToken,
        IIntegrationEventPublisher? integrationPublisher = null)
        where TEntry : class, IAuditOutboxEntry
    {
        var pending = await outbox
            .Where(e => e.DispatchedAt == null
                     && !e.IsTerminallyFailed
                     && (e.NextAttemptAt == null || e.NextAttemptAt <= now))
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var entry in pending)
        {
            try
            {
                var eventType = Type.GetType(entry.EventTypeName)
                    ?? throw new InvalidOperationException($"Unknown outbox event type '{entry.EventTypeName}'.");
                var payload = JsonSerializer.Deserialize(entry.PayloadJson, eventType)
                    ?? throw new InvalidOperationException("Outbox payload deserialized to null.");

                (object Publisher, Type Contract) target = entry.Channel switch
                {
                    OutboxChannel.Integration => (
                        integrationPublisher
                            ?? throw new InvalidOperationException(
                                $"Outbox entry {entry.Id} needs an IIntegrationEventPublisher but none was supplied to DispatchPendingAsync."),
                        typeof(IIntegrationEventPublisher)),
                    // Rows written before the Channel column existed default to "audit" - see
                    // AuditOutboxEntryConfiguration.
                    _ => (auditPublisher, typeof(IAuditEventPublisher)),
                };

                var publishMethod = target.Contract.GetMethod(nameof(IAuditEventPublisher.PublishAsync))!
                    .MakeGenericMethod(eventType);

                await (Task)publishMethod.Invoke(target.Publisher, [payload, cancellationToken])!;

                entry.DispatchedAt = now;
                entry.LastError = null;
            }
            catch (Exception ex)
            {
                entry.AttemptCount++;
                // Exception message only - never entry.PayloadJson, which may hold sensitive data.
                entry.LastError = ex.Message;

                if (entry.AttemptCount >= MaxAttemptsBeforeTerminal)
                {
                    entry.IsTerminallyFailed = true;
                    logger.LogError(ex,
                        "Outbox entry {EntryId} ({Channel}) terminally failed after {Attempts} attempts.",
                        entry.Id, entry.Channel, entry.AttemptCount);
                }
                else
                {
                    entry.NextAttemptAt = now + Backoff(entry.AttemptCount);
                    logger.LogWarning(ex,
                        "Outbox entry {EntryId} ({Channel}) dispatch failed, attempt {Attempt}, retrying at {NextAttemptAt}.",
                        entry.Id, entry.Channel, entry.AttemptCount, entry.NextAttemptAt);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, attempt)));
}
