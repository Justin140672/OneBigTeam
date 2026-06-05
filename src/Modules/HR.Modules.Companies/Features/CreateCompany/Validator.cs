using FluentValidation;

namespace HR.Modules.Companies.Features.CreateCompany;

internal sealed class CreateCompanyValidator : AbstractValidator<CreateCompanyRequest>
{
    public CreateCompanyValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(request => request.Slug)
            .MaximumLength(100)
            .Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .When(request => !string.IsNullOrWhiteSpace(request.Slug));
    }
}
