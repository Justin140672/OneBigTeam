using FluentValidation;

namespace HR.Modules.Recruitment.Features.ListBlockedInterviewOutcomeReconciliations;

internal sealed class ListBlockedInterviewOutcomeReconciliationsValidator : AbstractValidator<ListBlockedInterviewOutcomeReconciliationsRequest>
{
    public ListBlockedInterviewOutcomeReconciliationsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
