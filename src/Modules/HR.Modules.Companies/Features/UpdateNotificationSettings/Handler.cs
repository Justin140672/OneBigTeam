using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdateNotificationSettings;

/// <summary>
/// SET-06: updates the company's notification-channel settings. Requires "hr-settings:manage" —
/// the same policy UpdateHrSettings/UpdateRecruitmentSettings require.
/// </summary>
internal sealed class UpdateNotificationSettingsHandler(
    CompaniesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ICurrentUser currentUser)
{
    public async Task<Result<UpdateNotificationSettingsResponse>> HandleAsync(
        UpdateNotificationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies
            .Include(c => c.Settings)
            .SingleOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken);

        if (company is null)
            return Result.Failure<UpdateNotificationSettingsResponse>(
                Error.NotFound($"Company with id '{request.CompanyId}' was not found."));

        var now = clock.UtcNowOffset();

        var previousSettings = company.Settings is null
            ? null
            : new NotificationSettingsAuditSnapshot(
                company.Settings.EmailNotificationsEnabled,
                company.Settings.ScheduledRemindersEnabled);

        var settings = company.Settings ?? CompanySettings.CreateDefault(company.Id, now);
        settings.UpdateNotificationSettings(
            request.EmailNotificationsEnabled,
            request.ScheduledRemindersEnabled,
            now);

        company.SetSettings(settings, now);

        dbContext.Entry(settings).Property(s => s.Version).OriginalValue = request.Version;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<UpdateNotificationSettingsResponse>(
                Error.Conflict("Notification settings were changed by someone else. Reload the latest settings and try again."));
        }

        await auditEventPublisher.PublishAsync(
            new NotificationSettingsUpdatedAuditEvent(
                company.Id,
                currentUser.UserId,
                now,
                previousSettings,
                new NotificationSettingsAuditSnapshot(
                    settings.EmailNotificationsEnabled,
                    settings.ScheduledRemindersEnabled)),
            cancellationToken);

        return Result.Success(new UpdateNotificationSettingsResponse(
            company.Id,
            settings.EmailNotificationsEnabled,
            settings.ScheduledRemindersEnabled,
            settings.UpdatedAt,
            settings.Version));
    }
}
