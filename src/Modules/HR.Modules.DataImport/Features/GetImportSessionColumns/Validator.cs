using FluentValidation;

namespace HR.Modules.DataImport.Features.GetImportSessionColumns;

internal sealed class GetImportSessionColumnsValidator : AbstractValidator<GetImportSessionColumnsRequest>
{
    public GetImportSessionColumnsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ImportSessionId).NotEmpty();
    }
}
