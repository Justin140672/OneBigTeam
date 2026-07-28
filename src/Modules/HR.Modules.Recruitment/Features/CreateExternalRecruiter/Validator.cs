using FluentValidation;

namespace HR.Modules.Recruitment.Features.CreateExternalRecruiter;

internal sealed class CreateExternalRecruiterValidator : AbstractValidator<CreateExternalRecruiterRequest>
{
    public CreateExternalRecruiterValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.AgencyName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.ContactName)
            .MaximumLength(200)
            .When(r => !string.IsNullOrWhiteSpace(r.ContactName));

        RuleFor(r => r.ContactEmail)
            .EmailAddress()
            .MaximumLength(320)
            .When(r => !string.IsNullOrWhiteSpace(r.ContactEmail));

        RuleFor(r => r.ContactTelephone)
            .MaximumLength(50)
            .When(r => !string.IsNullOrWhiteSpace(r.ContactTelephone));

        RuleFor(r => r.Website)
            .MaximumLength(500)
            .When(r => !string.IsNullOrWhiteSpace(r.Website));

        RuleFor(r => r.Notes)
            .MaximumLength(4000)
            .When(r => !string.IsNullOrWhiteSpace(r.Notes));
    }
}
