namespace HR.Modules.Companies.Features.ExtendCustomerTrial;

internal sealed record ExtendCustomerTrialRequest
{
    public Guid CompanyId { get; init; }
    public DateTimeOffset NewTrialExpiresAt { get; init; }
    public string Reason { get; init; } = string.Empty;
}
