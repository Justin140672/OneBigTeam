using FluentValidation;

namespace HR.Modules.Reporting.Features.SaveReportView;

internal sealed class SaveReportViewValidator : AbstractValidator<SaveReportViewRequest>
{
    public SaveReportViewValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.ReportId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FilterCriteriaJson).NotEmpty();
    }
}
