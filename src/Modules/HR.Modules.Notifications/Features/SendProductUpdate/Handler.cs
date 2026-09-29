using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Notifications.Domain;
using HR.SharedKernel;

namespace HR.Modules.Notifications.Features.SendProductUpdate;

/// <summary>
/// Customer Release Notifications: a manual, deliberate platform-admin action (never wired into
/// deploy/CI) that raises a ProductUpdate notification for every active (UserProfile.IsActive)
/// Company Administrator of every active, non-expired/cancelled customer company — see
/// ProductUpdateRecipientResolver for the exact eligibility rules, shared with the preview endpoint
/// so the confirmation dialog's N/M count matches what actually gets sent.
///
/// Reuses the existing INotificationWriter.WriteAsync infrastructure (same bell/unread-count/
/// mark-as-read/click-navigate behaviour as every other notification type) rather than a parallel
/// delivery mechanism — one individual Notification row per recipient, so per-user read/unread
/// tracking works exactly as it does for every other notification type. Deliberately left additive:
/// a future "also send by email" only needs a NotificationChannelDefaults entry, and a future
/// "What's New" history only needs a new read query over the same rows — no rewrite required.
/// </summary>
internal sealed class SendProductUpdateHandler(
    IActiveCompanyDirectory activeCompanyDirectory,
    ISubscriptionStatusReader subscriptionStatusReader,
    ICompanyAdministratorDirectory companyAdministratorDirectory,
    INotificationWriter notificationWriter,
    IAuditEventPublisher auditEventPublisher,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<SendProductUpdateResponse>> HandleAsync(
        SendProductUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var recipients = await ProductUpdateRecipientResolver.ResolveAsync(
            activeCompanyDirectory, subscriptionStatusReader, companyAdministratorDirectory, cancellationToken);

        var now = clock.UtcNowOffset();

        var batchId = Guid.NewGuid();

        foreach (var recipient in recipients)
        {
            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                recipient.CompanyId,
                recipient.EmployeeId,
                request.Title,
                request.Message,
                batchId,
                NotificationType.ProductUpdate,
                NotificationPriority.Normal,
                now,
                cancellationToken,
                request.Url);
        }

        await auditEventPublisher.PublishAsync(
            new ProductUpdateSentAuditEvent(
                batchId, currentUser.UserId, request.Title, recipients.Count,
                recipients.Select(r => r.CompanyId).Distinct().Count(), now),
            cancellationToken);

        return Result.Success(new SendProductUpdateResponse(
            recipients.Count, recipients.Select(r => r.CompanyId).Distinct().Count()));
    }
}
