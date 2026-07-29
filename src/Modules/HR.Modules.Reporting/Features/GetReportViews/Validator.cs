using FluentValidation;

namespace HR.Modules.Reporting.Features.GetReportViews;

internal sealed class GetReportViewsValidator : AbstractValidator<GetReportViewsRequest>
{
    public GetReportViewsValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.ReportId).NotEmpty().MaximumLength(200);
    }
}
