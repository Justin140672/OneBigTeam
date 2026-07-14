using FluentValidation;

namespace HR.Modules.Documents.Features.ArchiveSharedCompanyDocument;

internal sealed class ArchiveSharedCompanyDocumentValidator : AbstractValidator<ArchiveSharedCompanyDocumentRequest>
{
    public ArchiveSharedCompanyDocumentValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.DocumentId).NotEmpty();

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MaximumLength(500);
    }
}
