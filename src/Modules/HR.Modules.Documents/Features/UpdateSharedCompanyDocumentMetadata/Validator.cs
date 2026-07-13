using FluentValidation;

namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentMetadata;

internal sealed class UpdateSharedCompanyDocumentMetadataValidator : AbstractValidator<UpdateSharedCompanyDocumentMetadataRequest>
{
    public UpdateSharedCompanyDocumentMetadataValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.DocumentId)
            .NotEmpty();

        RuleFor(r => r.CategoryId)
            .NotEmpty();

        RuleFor(r => r.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(2000)
            .When(r => r.Description is not null);
    }
}
