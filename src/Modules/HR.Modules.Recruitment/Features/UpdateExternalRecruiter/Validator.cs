using FluentValidation;

namespace HR.Modules.Recruitment.Features.UpdateExternalRecruiter;

internal sealed class UpdateExternalRecruiterValidator : AbstractValidator<UpdateExternalRecruiterRequest>
{
    public UpdateExternalRecruiterValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.ExternalRecruiterId)
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
