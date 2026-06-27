namespace HR.Modules.Employees.Features.ListRequiredDocumentsForPositionProfile;

internal sealed record ListRequiredDocumentsRequest
{
    public Guid CompanyId { get; init; }
    public Guid PositionProfileId { get; init; }
}
