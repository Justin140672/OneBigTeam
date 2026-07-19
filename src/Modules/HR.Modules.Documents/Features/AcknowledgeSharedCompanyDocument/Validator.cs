using FluentValidation;

namespace HR.Modules.Documents.Features.AcknowledgeSharedCompanyDocument;

internal sealed class AcknowledgeSharedCompanyDocumentValidator : AbstractValidator<AcknowledgeSharedCompanyDocumentRequest>
{
    public AcknowledgeSharedCompanyDocumentValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.DocumentId).NotEmpty();

        RuleFor(r => r.Confirmed)
            .Equal(true)
            .WithMessage("You must confirm that you have read and understood this document before it can be acknowledged.");
    }
}
