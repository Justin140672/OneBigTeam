using HR.SharedKernel;

namespace HR.Infrastructure.Abstractions;

public sealed record OrganisationDataExportCompletedIntegrationEvent(
    Guid CompanyId,
    Guid ExportId,
    Guid? RequestedByUserId,
    DateTimeOffset CompletedAt) : IIntegrationEvent;
