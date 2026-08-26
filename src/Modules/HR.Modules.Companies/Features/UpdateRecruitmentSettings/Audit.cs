using HR.SharedKernel;

namespace HR.Modules.Companies.Features.UpdateRecruitmentSettings;

internal sealed record RecruitmentSettingsAuditSnapshot(
    bool VacancyApprovalRequired,
    bool OfferApprovalRequired,
    int CandidateRetentionDays);

internal sealed record RecruitmentSettingsUpdatedAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    RecruitmentSettingsAuditSnapshot? PreviousSettings,
    RecruitmentSettingsAuditSnapshot CurrentSettings) : IAuditEvent
{
    string IAuditEvent.EventType => "recruitment-settings.updated";
    string IAuditEvent.EntityType => "CompanySettings";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorUserId => ActorUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Recruitment settings updated";
    object? IAuditEvent.Before => PreviousSettings;
    object? IAuditEvent.After => CurrentSettings;
    object? IAuditEvent.Metadata => null;
}
