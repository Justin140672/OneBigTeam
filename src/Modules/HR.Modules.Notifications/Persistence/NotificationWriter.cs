using Hangfire;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Jobs;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Persistence;

internal sealed class NotificationWriter(
    NotificationsDbContext dbContext,
    IBackgroundJobClient backgroundJobClient,
    IAuditEventPublisher auditPublisher,
    ICompanyNotificationSettingsReader notificationSettingsReader) : INotificationWriter
{
    /// <summary>
    /// NOT-03: template-based write path for the six NotificationType values registered in
    /// NotificationTemplateCatalogue. Required-token validation happens before any entity is added
    /// to the change tracker or SaveChangesAsync is called — a validation failure here means nothing
    /// is queued for delivery, per NOT-03's "missing required tokens fail before delivery is queued"
    /// acceptance criterion.
    /// </summary>
    public async Task<Result> WriteTemplatedAsync(
        Guid id,
        Guid companyId,
        Guid employeeId,
        NotificationType type,
        IReadOnlyDictionary<string, string> tokens,
        Guid sourceEntityId,
        NotificationPriority priority,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        if (!NotificationTemplateCatalogue.TryGet(type, out var template) || template is null)
        {
            throw new InvalidOperationException(
                $"No notification template is registered for NotificationType '{type}'. " +
                $"Use {nameof(WriteAsync)} with a pre-formatted string for types outside the NOT-03 template catalogue.");
        }

        // SET-06: a scheduled-reminder type is suppressed entirely (no in-app notification, no
        // email) while the company has ScheduledRemindersEnabled off — this is a deliberate no-op,
        // not a failure.
        var notificationSettings = await notificationSettingsReader.GetNotificationSettingsAsync(companyId, cancellationToken);
        if (NotificationChannelDefaults.IsScheduledReminder(type) && !notificationSettings.ScheduledRemindersEnabled)
            return Result.Success();

        var renderResult = NotificationTemplateRenderer.Render(template, tokens);
        if (renderResult.IsFailure)
            return Result.Failure(renderResult.Error);

        var rendered = renderResult.Value!;

        var actionUrl = NotificationActionRouteBuilder.BuildActionUrl(type, companyId, employeeId, sourceEntityId);
        var notification = Notification.Create(
            id, companyId, employeeId, rendered.InAppTitle, rendered.InAppBody, sourceEntityId, createdAt, type, priority, actionUrl);
        dbContext.Notifications.Add(notification);

        // SET-06: in-app notifications continue per the documented channel policy regardless of the
        // company's EmailNotificationsEnabled setting — only the Email channel below is gated by it
        // (mandatory/compliance types are never gated, even when disabled).
        var channel = NotificationChannelDefaults.GetChannel(type);
        var emailEligible = channel.HasFlag(NotificationChannel.Email) &&
            (notificationSettings.EmailNotificationsEnabled || NotificationChannelDefaults.IsMandatoryEmail(type));
        EmailDelivery? emailDelivery = null;
        if (emailEligible)
        {
            emailDelivery = EmailDelivery.CreateTemplated(
                Guid.NewGuid(), companyId, id, template.Version, rendered.EmailSubject, rendered.EmailBody, createdAt);
            dbContext.EmailDeliveries.Add(emailDelivery);
        }

        var created = await TrySaveIdempotentlyAsync(employeeId, sourceEntityId, type, cancellationToken);
        if (!created)
        {
            // OBT-REM-03 / OBT-REM-12: a concurrent or retried caller already created the
            // notification for this (employee, source entity, type) idempotency key. The original
            // winner is normally the one that publishes the audit event and enqueues the email — but
            // if that winner crashed after its SaveChangesAsync committed and before it finished
            // those two steps, neither would ever happen. Rather than silently no-op (the pre-REM-12
            // behaviour), repair any missing downstream work for the existing row before returning.
            await RepairExistingNotificationAsync(employeeId, sourceEntityId, type, cancellationToken);
            return Result.Success();
        }

        // NOT-05: creation audit — actor is the system, since this is shared infrastructure with
        // no reliable human actor at this call site (see NotificationsSystemActor doc comment).
        await auditPublisher.PublishAsync(new NotificationCreatedAuditEvent(
            companyId, id, employeeId, type, channel, createdAt), cancellationToken);

        if (emailDelivery is not null)
        {
            backgroundJobClient.Enqueue<EmailDeliveryJob>(job => job.SendAsync(id, companyId, null));
        }

        return Result.Success();
    }

    public async Task WriteAsync(
        Guid id,
        Guid companyId,
        Guid employeeId,
        string title,
        string? body,
        Guid sourceEntityId,
        NotificationType type,
        NotificationPriority priority,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        // SET-06: a scheduled-reminder type is suppressed entirely (no in-app notification, no
        // email) while the company has ScheduledRemindersEnabled off — this is a deliberate no-op.
        var notificationSettings = await notificationSettingsReader.GetNotificationSettingsAsync(companyId, cancellationToken);
        if (NotificationChannelDefaults.IsScheduledReminder(type) && !notificationSettings.ScheduledRemindersEnabled)
            return;

        var actionUrl = NotificationActionRouteBuilder.BuildActionUrl(type, companyId, employeeId, sourceEntityId);
        var notification = Notification.Create(id, companyId, employeeId, title, body, sourceEntityId, createdAt, type, priority, actionUrl);
        dbContext.Notifications.Add(notification);

        // NOT-02: channel-aware delivery. In-app is always written above (existing baseline
        // behaviour, unchanged); if this notification type also defaults to Email (see
        // NotificationChannelDefaults), an EmailDelivery row is persisted in the same transaction
        // as the notification and a Hangfire job is enqueued to perform the actual Postmark send
        // asynchronously — the caller (any of the 30+ handlers/jobs across the app that call
        // WriteAsync) never blocks on that external HTTP call.
        // SET-06: only the Email channel is gated by EmailNotificationsEnabled (mandatory/compliance
        // types are never gated) — in-app continues per the documented channel policy either way.
        var channel = NotificationChannelDefaults.GetChannel(type);
        var emailEligible = channel.HasFlag(NotificationChannel.Email) &&
            (notificationSettings.EmailNotificationsEnabled || NotificationChannelDefaults.IsMandatoryEmail(type));
        EmailDelivery? emailDelivery = null;
        if (emailEligible)
        {
            emailDelivery = EmailDelivery.Create(Guid.NewGuid(), companyId, id, createdAt);
            dbContext.EmailDeliveries.Add(emailDelivery);
        }

        var created = await TrySaveIdempotentlyAsync(employeeId, sourceEntityId, type, cancellationToken);
        if (!created)
        {
            // OBT-REM-03 / OBT-REM-12: a concurrent or retried caller won the race on the
            // (employee, source entity, type) unique key — repair any missing downstream work for
            // the existing row (see the WriteTemplatedAsync overload's identical comment) rather than
            // silently no-op.
            await RepairExistingNotificationAsync(employeeId, sourceEntityId, type, cancellationToken);
            return;
        }

        // NOT-05: creation audit — actor is the system, since this is shared infrastructure with
        // no reliable human actor at this call site (see NotificationsSystemActor doc comment).
        await auditPublisher.PublishAsync(new NotificationCreatedAuditEvent(
            companyId, id, employeeId, type, channel, createdAt), cancellationToken);

        if (emailDelivery is not null)
        {
            backgroundJobClient.Enqueue<EmailDeliveryJob>(job => job.SendAsync(id, companyId, null));
        }
    }

    /// <summary>
    /// Persists the pending <see cref="Notification"/> (and optional <see cref="EmailDelivery"/>)
    /// added to the change tracker by the caller. Returns <c>true</c> when this call inserted the
    /// row, <c>false</c> when a concurrent caller had already inserted a notification for the same
    /// <c>(employee_id, source_entity_id, type)</c> idempotency key (PostgreSQL 23505 on
    /// <c>IX_notifications_employee_id_source_entity_id_type</c>). Any other database error
    /// propagates unchanged.
    /// </summary>
    private async Task<bool> TrySaveIdempotentlyAsync(
        Guid employeeId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            PostgresUniqueViolation.Is(exception, "IX_notifications_employee_id_source_entity_id_type")
            || PostgresUniqueViolation.Is(exception, "IX_email_deliveries_notification_id")
            || PostgresUniqueViolation.Is(exception, "IX_email_deliveries_idempotency_key"))
        {
            // Detach the entities this losing caller tried (and failed) to insert so the shared
            // scoped DbContext is safe to reuse.
            foreach (var entry in dbContext.ChangeTracker.Entries<Notification>().ToList())
                entry.State = EntityState.Detached;
            foreach (var entry in dbContext.ChangeTracker.Entries<EmailDelivery>().ToList())
                entry.State = EntityState.Detached;

            return false;
        }
    }

    /// <summary>
    /// OBT-REM-12: called when a write loses the (employee_id, source_entity_id, type) uniqueness
    /// race — the notification (and, if channel-eligible, its EmailDelivery row) already exists from
    /// an earlier attempt. That earlier attempt is normally the one responsible for publishing the
    /// creation audit and enqueuing the delivery job, but if it crashed after its own commit and
    /// before finishing those steps, this repairs the gap:
    ///  - Re-publishes NotificationCreatedAuditEvent. Safe to call unconditionally: its EventId is
    ///    deterministic (== NotificationId), and both the audit staging table and the committed
    ///    audit table dedupe on that unique EventId — a duplicate publish is a guaranteed no-op.
    ///  - Re-enqueues EmailDeliveryJob for a still-Pending delivery. Safe to call unconditionally:
    ///    the job re-reads its own row, no-ops on Sent, and the xmin concurrency token on
    ///    EmailDelivery (see EmailDeliveryConfiguration) prevents two concurrent executions from
    ///    both sending.
    /// Never re-creates a Notification or EmailDelivery row — both already exist by definition here.
    /// </summary>
    private async Task RepairExistingNotificationAsync(
        Guid employeeId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Notifications
            .AsNoTracking()
            .SingleOrDefaultAsync(
                n => n.EmployeeId == employeeId && n.SourceEntityId == sourceEntityId && n.Type == type,
                cancellationToken);

        if (existing is null)
            return; // Should not happen (the unique-violation implies a row exists) — nothing to repair.

        var channel = NotificationChannelDefaults.GetChannel(type);
        await auditPublisher.PublishAsync(new NotificationCreatedAuditEvent(
            existing.CompanyId, existing.Id, existing.EmployeeId, existing.Type, channel, existing.CreatedAt),
            cancellationToken);

        var delivery = await dbContext.EmailDeliveries
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.NotificationId == existing.Id, cancellationToken);

        if (delivery is not null && delivery.Status == EmailDeliveryStatus.Pending)
        {
            backgroundJobClient.Enqueue<EmailDeliveryJob>(job => job.SendAsync(existing.Id, existing.CompanyId, null));
        }
    }

    public async Task<bool> ExistsAsync(
        Guid employeeId,
        Guid sourceEntityId,
        NotificationType type,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Notifications
            .AnyAsync(
                n => n.EmployeeId == employeeId && n.SourceEntityId == sourceEntityId && n.Type == type,
                cancellationToken);
    }

    public async Task<DateTimeOffset?> GetLastSentAtAsync(
        Guid employeeId,
        Guid sourceEntityId,
        NotificationType type,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.EmployeeId == employeeId && n.SourceEntityId == sourceEntityId && n.Type == type)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => (DateTimeOffset?)n.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> RemoveBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        NotificationType type,
        CancellationToken cancellationToken = default)
    {
        var matching = await dbContext.Notifications
            .Where(n => n.CompanyId == companyId && n.SourceEntityId == sourceEntityId && n.Type == type)
            .ToListAsync(cancellationToken);

        if (matching.Count == 0)
            return 0;

        dbContext.Notifications.RemoveRange(matching);
        await dbContext.SaveChangesAsync(cancellationToken);
        return matching.Count;
    }
}
