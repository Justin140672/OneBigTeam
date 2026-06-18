using FluentValidation;

namespace HR.Modules.Documents.Features.CreateDocumentType;

internal sealed class CreateDocumentTypeValidator : AbstractValidator<CreateDocumentTypeRequest>
{
    public CreateDocumentTypeValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(1000)
            .When(r => !string.IsNullOrWhiteSpace(r.Description));
    }
}
