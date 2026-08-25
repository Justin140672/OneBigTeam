using FluentValidation;

namespace HR.Modules.Documents.Features.UploadEmployeeDocumentVersion;

internal sealed class UploadEmployeeDocumentVersionValidator : AbstractValidator<UploadEmployeeDocumentVersionRequest>
{
    public UploadEmployeeDocumentVersionValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.EmployeeDocumentId).NotEmpty();

        RuleFor(r => r.File)
            .NotNull()
            .WithMessage("A file must be provided.");
    }
}
