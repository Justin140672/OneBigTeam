using FluentValidation;

namespace HR.Modules.Employees.Features.SuggestWorkEmail;

internal sealed class SuggestWorkEmailValidator : AbstractValidator<SuggestWorkEmailRequest>
{
    public SuggestWorkEmailValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.FirstName).MaximumLength(100);
        RuleFor(r => r.LastName).MaximumLength(100);
        RuleFor(r => r.Domain).MaximumLength(253);
    }
}
