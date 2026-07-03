using FluentValidation;
using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.CreateCompany;

internal sealed class CreateCompanyValidator : AbstractValidator<CreateCompanyRequest>
{
    public CreateCompanyValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleForEach(request => request.Addresses)
            .SetValidator(new CreateCompanyAddressValidator());

        RuleFor(request => request.Addresses)
            .Must(HaveUniqueAddressTypes)
            .WithMessage("Address types must be unique per company.");

        RuleFor(request => request.Addresses)
            .Must(HaveRegisteredOffice)
            .WithMessage("Registered Office address is required.");
    }

    private static bool HaveUniqueAddressTypes(IReadOnlyCollection<CreateCompanyAddressRequest> addresses)
    {
        return addresses
            .Select(address => address.Type)
            .Distinct()
            .Count() == addresses.Count;
    }

    private static bool HaveRegisteredOffice(IReadOnlyCollection<CreateCompanyAddressRequest> addresses)
    {
        return addresses.Any(address => address.Type == CompanyAddressType.RegisteredOffice);
    }

    private sealed class CreateCompanyAddressValidator : AbstractValidator<CreateCompanyAddressRequest>
    {
        public CreateCompanyAddressValidator()
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
