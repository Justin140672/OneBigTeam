using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.DownloadOrganisationDataExport;

/// <summary>
/// Story 2: a company administrator downloaded a completed organisation data export archive.
/// </summary>
internal sealed record OrganisationDataExportDownloadedAuditEvent(
    Guid CompanyId,
    Guid ExportId,
    Guid DownloadedByUserId,
    int DownloadCount,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "organisation-data-export.downloaded";
    string IAuditEvent.EntityType => "OrganisationDataExport";
    Guid IAuditEvent.EntityId => ExportId;
    Guid? IAuditEvent.ActorUserId => DownloadedByUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Organisation data export downloaded";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => new { ExportId, DownloadCount };
}
