using FluentValidation;

namespace HR.Modules.Reporting.Features.GetLatestOrganisationDataExport;

internal sealed class GetLatestOrganisationDataExportValidator : AbstractValidator<GetLatestOrganisationDataExportRequest>
{
    public GetLatestOrganisationDataExportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
