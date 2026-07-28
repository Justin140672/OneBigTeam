using FluentValidation;

namespace HR.Modules.Recruitment.Features.GetExternalRecruiterActivitySummary;

internal sealed class GetExternalRecruiterActivitySummaryValidator : AbstractValidator<GetExternalRecruiterActivitySummaryRequest>
{
    public GetExternalRecruiterActivitySummaryValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.ExternalRecruiterId)
            .NotEmpty();
    }
}
