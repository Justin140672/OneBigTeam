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

        await dbContext.SaveChangesAsync(cancellationToken);

        // NOT-05: creation audit — actor is the system, since this is shared infrastructure with
        // no reliable human actor at this call site (see NotificationsSystemActor doc comment).
        await auditPublisher.PublishAsync(new NotificationCreatedAuditEvent(
            companyId, id, employeeId, type, channel, createdAt), cancellationToken);

        if (emailDelivery is not null)
        {
            backgroundJobClient.Enqueue<EmailDeliveryJob>(job => job.SendAsync(id, null));
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

        await dbContext.SaveChangesAsync(cancellationToken);

        // NOT-05: creation audit — actor is the system, since this is shared infrastructure with
        // no reliable human actor at this call site (see NotificationsSystemActor doc comment).
        await auditPublisher.PublishAsync(new NotificationCreatedAuditEvent(
            companyId, id, employeeId, type, channel, createdAt), cancellationToken);

        if (emailDelivery is not null)
        {
            backgroundJobClient.Enqueue<EmailDeliveryJob>(job => job.SendAsync(id, null));
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
