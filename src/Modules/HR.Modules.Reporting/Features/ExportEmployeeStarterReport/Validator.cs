using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportEmployeeStarterReport;

internal sealed class ExportEmployeeStarterReportValidator : AbstractValidator<ExportEmployeeStarterReportRequest>
{
    public ExportEmployeeStarterReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();

        RuleFor(x => x.DateRangeEnd)
            .GreaterThanOrEqualTo(x => x.DateRangeStart!.Value)
            .When(x => x.DateRangeStart is not null && x.DateRangeEnd is not null);
    }
}
