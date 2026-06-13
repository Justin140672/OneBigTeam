using FluentValidation;

namespace HR.Modules.Leave.Features.UpdatePublicHoliday;

internal sealed class UpdatePublicHolidayValidator : AbstractValidator<UpdatePublicHolidayRequest>
{
    public UpdatePublicHolidayValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Date).NotEqual(default(DateOnly));
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.CountryCode).NotEmpty().MaximumLength(10);
    }
}
