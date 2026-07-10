using FluentValidation;

namespace HR.Modules.DataImport.Features.GetImportSession;

internal sealed class GetImportSessionValidator : AbstractValidator<GetImportSessionRequest>
{
    public GetImportSessionValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ImportSessionId).NotEmpty();
    }
}
