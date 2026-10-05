using FluentValidation;

namespace HR.Modules.Recruitment.Features.RetryInterviewOutcomeReconciliation;

internal sealed class RetryInterviewOutcomeReconciliationValidator : AbstractValidator<RetryInterviewOutcomeReconciliationRequest>
{
    public RetryInterviewOutcomeReconciliationValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ReconciliationId).NotEmpty();
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(500);
    }
}
