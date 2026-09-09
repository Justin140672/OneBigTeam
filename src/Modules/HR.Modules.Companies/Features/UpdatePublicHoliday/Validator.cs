using FluentValidation;
using HR.SharedKernel;

namespace HR.Modules.Companies.Features.UpdatePublicHoliday;

internal sealed class UpdatePublicHolidayValidator : AbstractValidator<UpdatePublicHolidayRequest>
{
    public UpdatePublicHolidayValidator()
    {
        // Ticket 2 item 3: a loaded concurrency version is mandatory on this protected update.
        RuleFor(r => r.ExpectedVersion).RequireLoadedVersion();

        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Date).NotEqual(default(DateOnly));
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.CountryCode).NotEmpty().MaximumLength(10);
    }
}
