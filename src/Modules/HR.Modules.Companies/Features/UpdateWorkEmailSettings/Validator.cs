using FluentValidation;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Companies.Features.UpdateWorkEmailSettings;

internal sealed class UpdateWorkEmailSettingsValidator : AbstractValidator<UpdateWorkEmailSettingsRequest>
{
    public UpdateWorkEmailSettingsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Version).GreaterThan(0);
        RuleFor(r => r.NamingConvention).IsInEnum();

        RuleFor(r => r.PrimaryDomain)
            .NotEmpty()
            .WithMessage("Primary email domain is required.");

        RuleFor(r => r.PrimaryDomain)
            .Must(BeValidDomain)
            .WithMessage("Primary email domain must be a valid domain such as example.co.uk.")
            .When(r => !string.IsNullOrWhiteSpace(r.PrimaryDomain));
    }

    private static bool BeValidDomain(string? domain) =>
        WorkEmailAddressBuilder.IsValidDomain(WorkEmailAddressBuilder.NormalizeDomain(domain));
}
