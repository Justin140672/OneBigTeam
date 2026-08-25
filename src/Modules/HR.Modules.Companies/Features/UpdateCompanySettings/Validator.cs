using FluentValidation;
using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed class UpdateCompanySettingsValidator : AbstractValidator<UpdateCompanySettingsRequest>
{
	public UpdateCompanySettingsValidator()
	{
		RuleFor(request => request.CompanyId)
			.NotEmpty();

		RuleFor(request => request.TimeZone)
			.NotEmpty()
			.MaximumLength(100)
			.Must(timeZone => CompanySettingsValidation.TryResolveTimeZone(timeZone, out _))
			.WithMessage("Time zone '{PropertyValue}' is not a recognised system time zone.");

		RuleFor(request => request.Locale)
			.NotEmpty()
			.MaximumLength(20)
			.Must(CompanySettingsValidation.IsSupportedLocale)
			.WithMessage("Locale '{PropertyValue}' is not a supported locale.");
	}
}
