using FluentValidation;

namespace HR.Modules.Recruitment.Features.UploadCandidateDocument;

internal sealed class UploadCandidateDocumentValidator : AbstractValidator<UploadCandidateDocumentRequest>
{
    public UploadCandidateDocumentValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.CandidateId)
            .NotEmpty();

        RuleFor(r => r.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.File)
            .NotNull()
            .WithMessage("A file must be provided.");

        RuleFor(r => r.Kind)
            .Must(k => Enum.TryParse<Domain.CandidateDocumentKind>(k, ignoreCase: true, out _))
            .When(r => !string.IsNullOrWhiteSpace(r.Kind))
            .WithMessage("Kind must be 'Cv' or 'Other'.");
    }
}
