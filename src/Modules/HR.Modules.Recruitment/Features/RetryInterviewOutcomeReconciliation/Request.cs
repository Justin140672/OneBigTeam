namespace HR.Modules.Recruitment.Features.RetryInterviewOutcomeReconciliation;

internal sealed record RetryInterviewOutcomeReconciliationRequest
{
    public Guid CompanyId { get; init; }
    public Guid ReconciliationId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
