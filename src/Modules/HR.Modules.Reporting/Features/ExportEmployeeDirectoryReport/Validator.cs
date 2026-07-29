using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportEmployeeDirectoryReport;

internal sealed class ExportEmployeeDirectoryReportValidator : AbstractValidator<ExportEmployeeDirectoryReportRequest>
{
    public ExportEmployeeDirectoryReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();

        RuleFor(x => x.DateRangeEnd)
            .GreaterThanOrEqualTo(x => x.DateRangeStart!.Value)
            .When(x => x.DateRangeStart is not null && x.DateRangeEnd is not null);
    }
}
