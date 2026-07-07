using FluentValidation;

namespace HR.Modules.DataImport.Features.ValidateImportSession;

internal sealed class ValidateImportSessionValidator : AbstractValidator<ValidateImportSessionRequest>
{
    public ValidateImportSessionValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ImportSessionId).NotEmpty();
    }
}
