using FluentValidation;

namespace HR.Modules.Employees.Features.ListOnboardingTemplates;

internal sealed class ListOnboardingTemplatesValidator : AbstractValidator<ListOnboardingTemplatesRequest>
{
    public ListOnboardingTemplatesValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();
    }
}
