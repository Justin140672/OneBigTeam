using FluentValidation;

namespace HR.Modules.DataImport.Features.ConfirmImportSession;

internal sealed class ConfirmImportSessionValidator : AbstractValidator<ConfirmImportSessionRequest>
{
    public ConfirmImportSessionValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ImportSessionId).NotEmpty();
    }
}
