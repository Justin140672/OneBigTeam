namespace HR.Modules.Employees.Features.RemoveRequiredDocumentFromPositionProfile;

internal sealed record RemoveRequiredDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid PositionProfileId { get; init; }
    public Guid Id { get; init; }
}
