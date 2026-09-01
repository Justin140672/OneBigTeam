using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.RequestOrganisationDataExport;

/// <summary>
/// Story 2: a company administrator requested a full organisation data export. Carries no exported
/// data — only the export id and requesting user.
/// </summary>
internal sealed record OrganisationDataExportRequestedAuditEvent(
    Guid CompanyId,
    Guid ExportId,
    Guid RequestedByUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "organisation-data-export.requested";
    string IAuditEvent.EntityType => "OrganisationDataExport";
    Guid IAuditEvent.EntityId => ExportId;
    Guid? IAuditEvent.ActorUserId => RequestedByUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Organisation data export requested";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => new { ExportId };
}
