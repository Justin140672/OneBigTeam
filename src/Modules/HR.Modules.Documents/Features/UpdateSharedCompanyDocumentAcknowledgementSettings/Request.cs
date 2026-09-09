namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAcknowledgementSettings;

internal sealed class UpdateSharedCompanyDocumentAcknowledgementSettingsRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
    public bool RequiresAcknowledgement { get; init; }
    public DateOnly? AcknowledgementDueDate { get; init; }
    public string? AcknowledgementStatement { get; init; }

    // Ticket 2 (optimistic concurrency) — see UpdateEmployeeProfileRequest.ExpectedVersion.
    public int? ExpectedVersion { get; init; }
}
