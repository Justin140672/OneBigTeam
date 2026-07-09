using FluentValidation;

namespace HR.Modules.DataImport.Features.GetImportPreview;

internal sealed class GetImportPreviewValidator : AbstractValidator<GetImportPreviewRequest>
{
    public GetImportPreviewValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ImportSessionId).NotEmpty();
    }
}
