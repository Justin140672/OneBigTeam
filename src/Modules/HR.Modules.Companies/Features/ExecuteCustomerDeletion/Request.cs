namespace HR.Modules.Companies.Features.ExecuteCustomerDeletion;

internal sealed record ExecuteCustomerDeletionRequest
{
    public Guid CompanyId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
