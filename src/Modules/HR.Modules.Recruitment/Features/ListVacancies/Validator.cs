using FluentValidation;

namespace HR.Modules.Recruitment.Features.ListVacancies;

internal sealed class ListVacanciesValidator : AbstractValidator<ListVacanciesRequest>
{
    public ListVacanciesValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();
    }
}
