using FluentValidation;

namespace HR.Modules.Leave.Features.CreatePublicHoliday;

internal sealed class CreatePublicHolidayValidator : AbstractValidator<CreatePublicHolidayRequest>
{
    public CreatePublicHolidayValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Date)
            .NotEqual(default(DateOnly));

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.CountryCode)
            .NotEmpty()
            .MaximumLength(10);
    }
}
