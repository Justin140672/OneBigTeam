namespace HR.Modules.Companies.Features.CancelCustomerDeletion;

internal sealed record CancelCustomerDeletionRequest
{
    public Guid CompanyId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
