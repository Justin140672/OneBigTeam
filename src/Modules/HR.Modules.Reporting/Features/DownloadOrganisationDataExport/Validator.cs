using FluentValidation;

namespace HR.Modules.Reporting.Features.DownloadOrganisationDataExport;

internal sealed class DownloadOrganisationDataExportValidator : AbstractValidator<DownloadOrganisationDataExportRequest>
{
    public DownloadOrganisationDataExportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.ExportId).NotEmpty();
    }
}
