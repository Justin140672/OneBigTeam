using FluentValidation;

namespace HR.Modules.Reporting.Features.RequestOrganisationDataExport;

internal sealed class RequestOrganisationDataExportValidator : AbstractValidator<RequestOrganisationDataExportRequest>
{
    public RequestOrganisationDataExportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
