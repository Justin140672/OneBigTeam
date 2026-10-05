namespace HR.Modules.Recruitment.Features.ListBlockedInterviewOutcomeReconciliations;

internal sealed record ListBlockedInterviewOutcomeReconciliationsRequest
{
    public Guid CompanyId { get; init; }
}
