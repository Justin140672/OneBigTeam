using FluentValidation;

namespace HR.Modules.DataImport.Features.UploadImportFile;

internal sealed class UploadImportFileValidator : AbstractValidator<UploadImportFileRequest>
{
    public UploadImportFileValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();

        RuleFor(r => r.EntityType)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.File)
            .NotNull()
            .WithMessage("A file must be provided.");
    }
}
