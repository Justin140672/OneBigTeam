using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportSicknessReport;

internal sealed class ExportSicknessReportValidator : AbstractValidator<ExportSicknessReportRequest>
{
    public ExportSicknessReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.GroupBy).IsInEnum();
        RuleFor(x => x.Format).IsInEnum();

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate!.Value)
            .When(x => x.StartDate is not null && x.EndDate is not null);
    }
}
