using FluentValidation;

namespace HR.Modules.DataImport.Features.ListImportSessions;

internal sealed class ListImportSessionsValidator : AbstractValidator<ListImportSessionsRequest>
{
    public ListImportSessionsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
