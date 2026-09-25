using FluentValidation;
using HR.SharedKernel;

namespace HR.Modules.Recruitment.Features.SetApplicationCv;

internal sealed class SetApplicationCvValidator : AbstractValidator<SetApplicationCvRequest>
{
    public SetApplicationCvValidator()
    {
        RuleFor(r => r.ExpectedVersion).RequireLoadedVersion();

        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.VacancyId).NotEmpty();
        RuleFor(r => r.ApplicationId).NotEmpty();

        // Null is meaningful (remove the reference); an explicit empty GUID is a malformed request.
        RuleFor(r => r.CvDocumentId)
            .NotEqual(Guid.Empty)
            .WithMessage("CvDocumentId must not be an empty identifier. Send null to remove the CV reference.")
            .When(r => r.CvDocumentId is not null);
    }
}
