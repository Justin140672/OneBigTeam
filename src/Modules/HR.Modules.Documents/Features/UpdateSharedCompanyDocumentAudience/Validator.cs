using FluentValidation;

namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAudience;

internal sealed class UpdateSharedCompanyDocumentAudienceValidator : AbstractValidator<UpdateSharedCompanyDocumentAudienceRequest>
{
    public UpdateSharedCompanyDocumentAudienceValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.DocumentId).NotEmpty();
    }
}
