using FluentValidation;

namespace HR.Modules.Documents.Features.ReissueSharedCompanyDocumentAcknowledgement;

internal sealed class ReissueSharedCompanyDocumentAcknowledgementValidator
    : AbstractValidator<ReissueSharedCompanyDocumentAcknowledgementRequest>
{
    public ReissueSharedCompanyDocumentAcknowledgementValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.DocumentId).NotEmpty();
    }
}
