using FluentValidation;
using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.UpdateCompany;

internal sealed class UpdateCompanyValidator : AbstractValidator<UpdateCompanyRequest>
{
    private static readonly System.Text.RegularExpressions.Regex HexColorRegex =
        new("^#[0-9A-Fa-f]{6}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public UpdateCompanyValidator()
    {
        RuleFor(request => request.Id)
            .NotEmpty();

        RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleForEach(request => request.Addresses)
            .SetValidator(new UpdateCompanyAddressValidator());

        RuleFor(request => request.Addresses)
            .Must(HaveUniqueAddressTypes)
            .WithMessage("Address types must be unique per company.");

        RuleFor(request => request.Addresses)
            .Must(HaveRegisteredOffice)
            .WithMessage("Registered Office address is required.");

        When(request => request.Branding is not null, () =>
        {
            RuleFor(request => request.Branding!.PrimaryColor)
                .NotEmpty()
                .Matches(HexColorRegex)
                .WithMessage("Primary color must be a valid 6-digit hex color (e.g. #1A2B3C).");

            RuleFor(request => request.Branding!.SecondaryColor)
                .NotEmpty()
                .Matches(HexColorRegex)
                .WithMessage("Secondary color must be a valid 6-digit hex color (e.g. #1A2B3C).");

            RuleFor(request => request.Branding!.AccentColor)
                .NotEmpty()
                .Matches(HexColorRegex)
                .WithMessage("Accent color must be a valid 6-digit hex color (e.g. #1A2B3C).");
        });
    }

    private static bool HaveUniqueAddressTypes(IReadOnlyCollection<UpdateCompanyAddressRequest> addresses)
        => addresses.Select(address => address.Type).Distinct().Count() == addresses.Count;

    private static bool HaveRegisteredOffice(IReadOnlyCollection<UpdateCompanyAddressRequest> addresses)
        => addresses.Any(address => address.Type == CompanyAddressType.RegisteredOffice);

    private sealed class UpdateCompanyAddressValidator : AbstractValidator<UpdateCompanyAddressRequest>
    {
        public UpdateCompanyAddressValidator()
        {
            RuleFor(address => address.Type)
                .IsInEnum();

            RuleFor(address => address.Line1)
                .NotEmpty()
                .MaximumLength(200);

            RuleFor(address => address.Line2)
                .MaximumLength(200);

            RuleFor(address => address.City)
                .NotEmpty()
                .MaximumLength(100);

            RuleFor(address => address.Region)
                .MaximumLength(100);

            RuleFor(address => address.PostalCode)
                .MaximumLength(20);

            RuleFor(address => address.CountryCode)
                .NotEmpty()
                .Length(2)
                .Matches("^[A-Za-z]{2}$");
        }
    }
}
