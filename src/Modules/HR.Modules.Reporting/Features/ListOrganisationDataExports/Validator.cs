using FluentValidation;

namespace HR.Modules.Reporting.Features.ListOrganisationDataExports;

internal sealed class ListOrganisationDataExportsValidator : AbstractValidator<ListOrganisationDataExportsRequest>
{
    public ListOrganisationDataExportsValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
