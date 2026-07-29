using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportEmployeeLeaverReport;

internal sealed class ExportEmployeeLeaverReportValidator : AbstractValidator<ExportEmployeeLeaverReportRequest>
{
    public ExportEmployeeLeaverReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();

        RuleFor(x => x.DateRangeEnd)
            .GreaterThanOrEqualTo(x => x.DateRangeStart!.Value)
            .When(x => x.DateRangeStart is not null && x.DateRangeEnd is not null);
    }
}
