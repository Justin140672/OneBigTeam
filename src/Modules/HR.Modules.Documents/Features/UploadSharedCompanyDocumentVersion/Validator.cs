using FluentValidation;

namespace HR.Modules.Documents.Features.UploadSharedCompanyDocumentVersion;

internal sealed class UploadSharedCompanyDocumentVersionValidator : AbstractValidator<UploadSharedCompanyDocumentVersionRequest>
{
    public UploadSharedCompanyDocumentVersionValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.DocumentId).NotEmpty();

        RuleFor(r => r.VersionNote)
            .NotEmpty()
            .MaximumLength(1000);

        RuleFor(r => r.File)
            .NotNull()
            .WithMessage("A file must be provided.");
    }
}
