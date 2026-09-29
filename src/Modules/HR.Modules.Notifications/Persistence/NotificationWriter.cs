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
            await RepairExistingNotificationAsync(employeeId, sourceEntityId, type, cancellationToken);
            return Result.Success();
        }

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
        CancellationToken cancellationToken = default,
        string? actionUrl = null)
    {
        var notificationSettings = await notificationSettingsReader.GetNotificationSettingsAsync(companyId, cancellationToken);
        if (NotificationChannelDefaults.IsScheduledReminder(type) && !notificationSettings.ScheduledRemindersEnabled)
            return;

        actionUrl = actionUrl is not null
            ? NotificationActionRouteBuilder.EnforceRelative(actionUrl)
            : NotificationActionRouteBuilder.BuildActionUrl(type, companyId, employeeId, sourceEntityId);
        var notification = Notification.Create(id, companyId, employeeId, title, body, sourceEntityId, createdAt, type, priority, actionUrl);
        dbContext.Notifications.Add(notification);

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
            await RepairExistingNotificationAsync(employeeId, sourceEntityId, type, cancellationToken);
            return;
        }

        await auditPublisher.PublishAsync(new NotificationCreatedAuditEvent(
            companyId, id, employeeId, type, channel, createdAt), cancellationToken);

        if (emailDelivery is not null)
        {
            backgroundJobClient.Enqueue<EmailDeliveryJob>(job => job.SendAsync(id, companyId, null));
        }
    }

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
            foreach (var entry in dbContext.ChangeTracker.Entries<Notification>().ToList())
                entry.State = EntityState.Detached;
            foreach (var entry in dbContext.ChangeTracker.Entries<EmailDelivery>().ToList())
                entry.State = EntityState.Detached;

            return false;
        }
    }

    private async Task RepairExistingNotificationAsync(
        Guid employeeId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Notifications
            .AsNoTracking()
            .SingleOrDefaultAsync(
                n => n.EmployeeId == employeeId && n.SourceEntityId == sourceEntityId && n.Type == type,
                cancellationToken);

        if (existing is null)
            return;

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
