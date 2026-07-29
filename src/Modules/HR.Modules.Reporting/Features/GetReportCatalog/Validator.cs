using FluentValidation;

namespace HR.Modules.Reporting.Features.GetReportCatalog;

internal sealed class GetReportCatalogValidator : AbstractValidator<GetReportCatalogRequest>
{
    public GetReportCatalogValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
