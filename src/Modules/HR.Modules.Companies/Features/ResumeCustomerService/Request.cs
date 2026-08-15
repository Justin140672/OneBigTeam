namespace HR.Modules.Companies.Features.ResumeCustomerService;

internal sealed record ResumeCustomerServiceRequest
{
    public Guid CompanyId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
