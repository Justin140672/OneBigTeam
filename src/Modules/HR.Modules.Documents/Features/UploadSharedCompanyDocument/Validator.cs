using FluentValidation;

namespace HR.Modules.Documents.Features.UploadSharedCompanyDocument;

internal sealed class UploadSharedCompanyDocumentValidator : AbstractValidator<UploadSharedCompanyDocumentRequest>
{
    public UploadSharedCompanyDocumentValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.CategoryId).NotEmpty();

        RuleFor(r => r.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(2000)
            .When(r => !string.IsNullOrWhiteSpace(r.Description));

        RuleFor(r => r.File)
            .NotNull()
            .WithMessage("A file must be provided.");

        RuleFor(r => r.AcknowledgementStatement)
            .MaximumLength(1000)
            .When(r => !string.IsNullOrWhiteSpace(r.AcknowledgementStatement));
    }
}
