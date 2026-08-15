namespace HR.Modules.Companies.Features.ForceCustomerReadOnly;

internal sealed record ForceCustomerReadOnlyRequest
{
    public Guid CompanyId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
