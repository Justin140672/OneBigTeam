using FluentValidation;

namespace HR.Modules.DataImport.Features.ValidateImportSession;

internal sealed class ValidateImportSessionValidator : AbstractValidator<ValidateImportSessionRequest>
{
    public ValidateImportSessionValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ImportSessionId).NotEmpty();

        RuleForEach(r => r.ColumnMapping)
            .Must(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
            .WithMessage("Column mapping entries must have a non-empty target field and header name.")
            .When(r => r.ColumnMapping is { Count: > 0 });
    }
}
