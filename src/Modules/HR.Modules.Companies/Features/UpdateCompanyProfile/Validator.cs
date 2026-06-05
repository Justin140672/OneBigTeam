using FluentValidation;

namespace HR.Modules.Companies.Features.UpdateCompanyProfile;

internal sealed class UpdateCompanyProfileValidator : AbstractValidator<UpdateCompanyProfileRequest>
{
    public UpdateCompanyProfileValidator()
    {
        RuleFor(request => request.Id)
            .NotEmpty();

        RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(200);
    }
}