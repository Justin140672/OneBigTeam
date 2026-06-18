using FluentValidation;

namespace HR.Modules.Documents.Features.UploadEmployeeDocument;

internal sealed class UploadEmployeeDocumentValidator : AbstractValidator<UploadEmployeeDocumentRequest>
{
    public UploadEmployeeDocumentValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.DocumentTypeId).NotEmpty();

        RuleFor(r => r.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(1000)
            .When(r => !string.IsNullOrWhiteSpace(r.Description));

        RuleFor(r => r.File)
            .NotNull()
            .WithMessage("A file must be provided.");
    }
}
