using FluentValidation;

namespace HR.Modules.Employees.Features.ImportCompensationChanges;

internal sealed class ImportCompensationChangesValidator : AbstractValidator<ImportCompensationChangesRequest>
{
    private static readonly string[] AllowedExtensions = [".xlsx"];

    private static readonly string[] AllowedContentTypes =
    [
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    ];

    public ImportCompensationChangesValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.File)
            .NotNull()
            .WithMessage("A file is required.");

        RuleFor(r => r.File)
            .Must(f => f.Length > 0)
            .WithMessage("File must not be empty.")
            .When(r => r.File is not null);

        RuleFor(r => r.File)
            .Must(f => AllowedExtensions.Contains(Path.GetExtension(f.FileName), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Only .xlsx files are accepted.")
            .When(r => r.File is not null);

        RuleFor(r => r.File)
            .Must(f => AllowedContentTypes.Contains(f.ContentType.Split(';')[0].Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Only .xlsx files are accepted.")
            .When(r => r.File is not null);
    }
}
