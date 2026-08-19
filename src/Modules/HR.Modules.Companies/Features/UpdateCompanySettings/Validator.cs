using FluentValidation;

namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed class UpdateCompanySettingsValidator : AbstractValidator<UpdateCompanySettingsRequest>
{
	public UpdateCompanySettingsValidator()
	{
		RuleFor(request => request.CompanyId)
			.NotEmpty();

		RuleFor(request => request.TimeZone)
			.NotEmpty()
			.MaximumLength(100);

		RuleFor(request => request.Locale)
			.NotEmpty()
			.MaximumLength(20);
	}
}
