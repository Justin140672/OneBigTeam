using FluentValidation;

namespace HR.Modules.DataImport.Features.ExportImportErrors;

internal sealed class ExportImportErrorsValidator : AbstractValidator<ExportImportErrorsRequest>
{
    public ExportImportErrorsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ImportSessionId).NotEmpty();
    }
}
