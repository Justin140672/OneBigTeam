using HR.SharedKernel;

namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Story 2: raised by the Infrastructure background job once an organisation data export ZIP has
/// been built and stored, so the Notifications module can tell the requesting company administrator
/// their download is ready. Lives in Abstractions (rather than a Reporting.Contracts project) so
/// both HR.Infrastructure (publisher) and HR.Modules.Notifications (consumer) can reference it
/// without a module-to-module dependency.
/// </summary>
public sealed record OrganisationDataExportCompletedIntegrationEvent(
    Guid CompanyId,
    Guid ExportId,
    Guid? RequestedByUserId,
    DateTimeOffset CompletedAt) : IIntegrationEvent;
