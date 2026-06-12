using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed record CompanySettingsAuditSnapshot(
    string TimeZone,
    string Locale,
    WorkingDays WorkingDays,
    decimal HoursPerDay,
    int LeaveYearStartMonth,
    decimal DefaultHolidayAllowance,
    int ProbationMonths,
    bool ExcludePublicHolidaysFromLeave);

internal sealed record CompanySettingsUpdatedAuditEvent(
    Guid CompanyId,
    string? ActorId,
    DateTimeOffset OccurredAt,
    CompanySettingsAuditSnapshot? PreviousSettings,
    CompanySettingsAuditSnapshot CurrentSettings);

internal interface ICompanyAuditEventPublisher
{
    Task PublishCompanySettingsUpdatedAsync(
        CompanySettingsUpdatedAuditEvent auditEvent,
        CancellationToken cancellationToken);
}

internal sealed class LoggerCompanyAuditEventPublisher(
    ILogger<LoggerCompanyAuditEventPublisher> logger) : ICompanyAuditEventPublisher
{
    public Task PublishCompanySettingsUpdatedAsync(
        CompanySettingsUpdatedAuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "AuditEvent company.settings.updated: CompanyId={CompanyId}, ActorId={ActorId}, OccurredAt={OccurredAt}, Previous={Previous}, Current={Current}",
            auditEvent.CompanyId,
            auditEvent.ActorId,
            auditEvent.OccurredAt,
            auditEvent.PreviousSettings,
            auditEvent.CurrentSettings);

        return Task.CompletedTask;
    }
}
