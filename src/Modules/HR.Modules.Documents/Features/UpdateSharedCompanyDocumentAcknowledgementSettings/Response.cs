namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAcknowledgementSettings;

internal sealed record UpdateSharedCompanyDocumentAcknowledgementSettingsResponse(
    Guid Id,
    Guid CompanyId,
    bool RequiresAcknowledgement,
    DateOnly? AcknowledgementDueDate,
    string? AcknowledgementStatement,
    DateTimeOffset UpdatedAt);
