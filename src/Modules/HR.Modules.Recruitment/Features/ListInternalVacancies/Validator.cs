using FluentValidation;

namespace HR.Modules.Recruitment.Features.ListInternalVacancies;

internal sealed class ListInternalVacanciesValidator : AbstractValidator<ListInternalVacanciesRequest>
{
    public ListInternalVacanciesValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();
    }
}
