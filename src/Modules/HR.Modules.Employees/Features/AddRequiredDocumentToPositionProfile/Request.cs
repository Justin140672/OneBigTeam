namespace HR.Modules.Employees.Features.AddRequiredDocumentToPositionProfile;

internal sealed record AddRequiredDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid PositionProfileId { get; init; }
    public Guid DocumentTypeId { get; init; }
    public bool IsMandatory { get; init; }
    public int? DueDaysAfterStart { get; init; }
    public bool RequiresExpiryDate { get; init; }
}
