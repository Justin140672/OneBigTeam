using FluentValidation;

namespace HR.Modules.Employees.Features.RequestPersonalDetailsChange;

internal sealed class RequestPersonalDetailsChangeValidator : AbstractValidator<RequestPersonalDetailsChangeRequest>
{
    public RequestPersonalDetailsChangeValidator()
    {
        RuleFor(x => x.Notes)
            .NotEmpty().WithMessage("Please describe the change you'd like to make.")
            .MaximumLength(2000).WithMessage("Notes must be 2000 characters or fewer.");
    }
}
