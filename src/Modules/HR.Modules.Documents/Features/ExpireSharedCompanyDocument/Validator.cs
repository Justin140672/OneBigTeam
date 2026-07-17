using FluentValidation;

namespace HR.Modules.Documents.Features.ExpireSharedCompanyDocument;

internal sealed class ExpireSharedCompanyDocumentValidator : AbstractValidator<ExpireSharedCompanyDocumentRequest>
{
    public ExpireSharedCompanyDocumentValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.DocumentId).NotEmpty();
    }
}
