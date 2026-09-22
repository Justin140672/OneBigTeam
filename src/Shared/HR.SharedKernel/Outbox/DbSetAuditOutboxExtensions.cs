using System.Text.Json;
using HR.SharedKernel.ExecutionContext;
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
        DateTimeOffset now,
        IExecutionContextAccessor? executionContextAccessor = null)
        where TEntry : class, IAuditOutboxEntry, new() =>
        Enqueue(outbox, OutboxChannel.Audit, auditEvent, companyId, now, executionContextAccessor);

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
        DateTimeOffset now,
        IExecutionContextAccessor? executionContextAccessor = null)
        where TEntry : class, IAuditOutboxEntry, new() =>
        Enqueue(outbox, OutboxChannel.Integration, integrationEvent, companyId, now, executionContextAccessor);

    private static void Enqueue<TEntry, TEvent>(
        DbSet<TEntry> outbox, string channel, TEvent @event, Guid companyId, DateTimeOffset now,
        IExecutionContextAccessor? executionContextAccessor)
        where TEntry : class, IAuditOutboxEntry, new()
    {
        // Ticket 23 (P2): capture whatever execution context is ambient right now (the HTTP request
        // or integration-event handler that is staging this entry) so durable redelivery after a
        // crash/restart can restore the same correlation/causation identity rather than inventing a
        // new one. Rows staged with nothing ambient (e.g. a very old call site, or a context genuinely
        // not yet established) simply get a fresh MessageId and null Correlation/CausationId -
        // callers are never required to pass metadata explicitly, satisfying the "avoid manually
        // adding correlation params to every event constructor" guardrail.
        var ambient = executionContextAccessor?.Current;

        outbox.Add(new TEntry
        {
            Id = Guid.NewGuid(),
            Channel = channel,
            EventTypeName = typeof(TEvent).AssemblyQualifiedName!,
            PayloadJson = JsonSerializer.Serialize(@event),
            CompanyId = companyId,
            CreatedAt = now,
            CorrelationId = ambient is null ? null : CorrelationIdGuid.Derive(ambient.CorrelationId),
            CausationId = ambient?.MessageId,
            MessageId = Guid.NewGuid(),
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
        IIntegrationEventPublisher? integrationPublisher = null,
        IExecutionContextAccessor? executionContextAccessor = null)
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

                // Ticket 23 (P2): restore the persisted metadata as the ambient execution context for
                // the duration of this redelivery (background job / reconciliation, possibly after a
                // process restart) - a retry of the same row therefore keeps its original
                // message/correlation/causation ids rather than being assigned fresh ones. Rows
                // written before these columns existed (all null) fall back to a fresh root context
                // rather than throwing, so they remain dispatchable.
                var restoredContext = entry.CorrelationId is { } correlationId
                    ? ExecutionContextInfo.Restore(
                        correlationId.ToString("D"),
                        entry.MessageId ?? Guid.NewGuid(),
                        entry.CausationId,
                        ExecutionOrigin.ReconciliationJob)
                    : ExecutionContextInfo.NewRoot(ExecutionOrigin.ReconciliationJob);

                using (executionContextAccessor?.Push(restoredContext))
                {
                    await (Task)publishMethod.Invoke(target.Publisher, [payload, cancellationToken])!;
                }

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
