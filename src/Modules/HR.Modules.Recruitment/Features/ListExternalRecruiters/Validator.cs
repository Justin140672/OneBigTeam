using FluentValidation;

namespace HR.Modules.Recruitment.Features.ListExternalRecruiters;

internal sealed class ListExternalRecruitersValidator : AbstractValidator<ListExternalRecruitersRequest>
{
    public ListExternalRecruitersValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, 200);
    }
}
