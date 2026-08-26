using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdateDocumentReminderSettings;

/// <summary>
/// SET-07: updates the company's document expiry reminder schedule. Requires "hr-settings:manage" —
/// the same policy UpdateHrSettings/UpdateRecruitmentSettings/UpdateNotificationSettings require.
/// </summary>
internal sealed class UpdateDocumentReminderSettingsHandler(
    CompaniesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ICurrentUser currentUser)
{
    public async Task<Result<UpdateDocumentReminderSettingsResponse>> HandleAsync(
        UpdateDocumentReminderSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies
            .Include(c => c.Settings)
            .SingleOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken);

        if (company is null)
            return Result.Failure<UpdateDocumentReminderSettingsResponse>(
                Error.NotFound($"Company with id '{request.CompanyId}' was not found."));

        var now = clock.UtcNowOffset();

        var previousSettings = company.Settings is null
            ? null
            : new DocumentReminderSettingsAuditSnapshot(
                company.Settings.DocumentRemindersEnabled,
                company.Settings.DocumentReminderOffsetDays1,
                company.Settings.DocumentReminderOffsetDays2,
                company.Settings.DocumentReminderOffsetDays3);

        var settings = company.Settings ?? CompanySettings.CreateDefault(company.Id, now);
        settings.UpdateDocumentReminderSettings(
            request.RemindersEnabled,
            request.OffsetDays1,
            request.OffsetDays2,
            request.OffsetDays3,
            now);

        company.SetSettings(settings, now);

        dbContext.Entry(settings).Property(s => s.Version).OriginalValue = request.Version;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<UpdateDocumentReminderSettingsResponse>(
                Error.Conflict("Document reminder settings were changed by someone else. Reload the latest settings and try again."));
        }

        await auditEventPublisher.PublishAsync(
            new DocumentReminderSettingsUpdatedAuditEvent(
                company.Id,
                currentUser.UserId,
                now,
                previousSettings,
                new DocumentReminderSettingsAuditSnapshot(
                    settings.DocumentRemindersEnabled,
                    settings.DocumentReminderOffsetDays1,
                    settings.DocumentReminderOffsetDays2,
                    settings.DocumentReminderOffsetDays3)),
            cancellationToken);

        return Result.Success(new UpdateDocumentReminderSettingsResponse(
            company.Id,
            settings.DocumentRemindersEnabled,
            settings.DocumentReminderOffsetDays1,
            settings.DocumentReminderOffsetDays2,
            settings.DocumentReminderOffsetDays3,
            settings.UpdatedAt,
            settings.Version));
    }
}
