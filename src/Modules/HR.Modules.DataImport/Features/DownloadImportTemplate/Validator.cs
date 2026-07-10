using FluentValidation;

namespace HR.Modules.DataImport.Features.DownloadImportTemplate;

internal sealed class DownloadImportTemplateValidator : AbstractValidator<DownloadImportTemplateRequest>
{
    public DownloadImportTemplateValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
