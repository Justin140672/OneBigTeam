using FluentValidation;

namespace HR.Modules.Documents.Features.UpdateDocumentType;

internal sealed class UpdateDocumentTypeValidator : AbstractValidator<UpdateDocumentTypeRequest>
{
    public UpdateDocumentTypeValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.DocumentTypeId)
            .NotEmpty();

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(1000)
            .When(r => !string.IsNullOrWhiteSpace(r.Description));
    }
}
